using System.Drawing;
using System.Runtime.CompilerServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Superpower1v5.Support;

namespace Superpower1v5.Managers;

public sealed class EffectsManager
{
    private sealed class Scalar(float baseline)
    {
        public float Baseline { get; } = baseline;
        public Dictionary<string, float> Multipliers { get; } = new();
        public float Multiplier => Multipliers.Values.Aggregate(1f, (a, b) => a * b);
    }
    private sealed record Outline(uint Target, uint Viewer, uint Relay, uint Glow, uint RelayIndex, uint GlowIndex);
    private readonly Dictionary<uint, Scalar> _scalars = new();
    // Keep invisible players and equipment networked so existing client references stay valid.
    private const uint HiddenEffects=(1u<<5)|(1u<<10);
    private readonly Dictionary<uint, (Color color, RenderMode_t mode,float shadow,uint effects)> _colors = new();
    private readonly Dictionary<uint, (int word, uint bit, bool hadBit, bool spotted)> _radar = new();
    private sealed class RadarBaseline(bool spotted)
    { public bool Spotted { get; }=spotted; public HashSet<EffectsManager> Owners { get; }=[]; }
    private static readonly Dictionary<uint,RadarBaseline> SharedRadar=new();
    private readonly Dictionary<uint, Outline> _outlines = new();
    private Outline[] _transmitOutlines = [];
    private sealed record Concealed(uint Carrier,uint[] Indices,uint[] CosmeticIndices,uint[] InEyeIndices,uint[] InEyeViewers)
    {
        public uint[] SuppressedFor(uint viewer) => viewer==Carrier ? [] :
            InEyeViewers.Contains(viewer) ? InEyeIndices : CosmeticIndices;
    }
    private sealed record HiddenGloves(uint Pawn,ulong ItemId,ushort Definition,byte Revision);
    private Concealed? _concealed;
    private HiddenGloves? _hiddenGloves;
    public int OutlineCount => _outlines.Count;
    public int HiddenEntityCount => Volatile.Read(ref _concealed)?.Indices.Length ?? 0;
    public bool HiddenFrom(uint viewer,uint entityIndex) => Volatile.Read(ref _concealed) is { } hidden && hidden.Carrier!=viewer && Array.IndexOf(hidden.Indices,entityIndex)>=0;
    public IReadOnlyList<CBaseModelEntity> AttachedEntities(CCSPlayerPawn pawn)
    {
        var models=new Dictionary<uint,CBaseModelEntity> { [pawn.EntityHandle.Raw]=pawn };
        if (pawn.WeaponServices is { } weapons)
            foreach (var handle in weapons.MyWeapons)
                if (handle.Value is { IsValid:true } weapon && weapon.OwnerEntity.Raw==pawn.EntityHandle.Raw)
                    models[weapon.EntityHandle.Raw]=weapon;
        foreach (var handle in pawn.MyWearables)
            if (handle.Value is { IsValid:true } wearable)
                models[wearable.EntityHandle.Raw]=wearable;
        var roots=models.Values.ToArray();
        foreach (var model in roots) AddChildren(model.CBodyComponent?.SceneNode?.Child,models,0);
        return models.Values.ToArray();
    }
    private static void AddChildren(CGameSceneNode? child,Dictionary<uint,CBaseModelEntity> models,int depth)
    {
        if (depth>=8) return;
        for (var count=0;child is not null && count<64;child=child.NextSibling,count++)
        {
            if (child.Owner is not { IsValid:true } entity || !IsEquipmentModel(entity.DesignerName)) continue;
            models.TryAdd(entity.EntityHandle.Raw,entity.As<CBaseModelEntity>());
            AddChildren(child.Child,models,depth+1);
        }
    }
    private static bool IsEquipmentModel(string name) =>
        name is "prop_dynamic" or "prop_dynamic_override" or "weaponworldmodel" or "weapon_world_model" or
        "wearable" or "wearable_item" or "econ_wearable" or "econ_entity" ||
        name.StartsWith("weapon_",StringComparison.Ordinal) && !name.Contains("viewmodel",StringComparison.Ordinal);
    public void Conceal(CCSPlayerPawn carrier,CCSPlayerController owner,IReadOnlyList<CBaseModelEntity> attached,bool fullyHidden)
    {
        SuppressGloves(carrier);
        var cosmeticIndices=new HashSet<uint>();
        var inEyeIndices=new HashSet<uint>();
        var activeWeapon=carrier.WeaponServices?.ActiveWeapon.Raw;
        foreach (var weapon in attached.OfType<CBasePlayerWeapon>())
        {
            var bomb=weapon.DesignerName=="weapon_c4";
            if (weapon.OwnerEntity.Raw!=carrier.EntityHandle.Raw || !bomb && !HasCosmeticDisplay(weapon)) continue;
            var indices=new HashSet<uint> { weapon.Index };
            AddCosmeticAttachments(weapon.CBodyComponent?.SceneNode?.Child,indices,0);
            if (weapon.EffectEntity.Value is { IsValid:true } effect && IsCosmeticAttachment(effect.DesignerName))
            {
                indices.Add(effect.Index);
                AddCosmeticAttachments(effect.CBodyComponent?.SceneNode?.Child,indices,0);
            }
            if (bomb)
                foreach (var charm in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("keychain_module"))
                    if (charm.IsValid && charm.OwnerEntity.Raw==weapon.EntityHandle.Raw)
                        indices.Add(charm.Index);
            cosmeticIndices.UnionWith(indices);
            // Preserve spectator weapon references. Stowed C4 still needs its third-person light suppressed.
            if (bomb && weapon.EntityHandle.Raw!=activeWeapon) inEyeIndices.UnionWith(indices);
        }
        var inEyeViewers=new List<uint>();
        if (cosmeticIndices.Count!=0)
            foreach (var player in Utilities.GetPlayers())
                if (player.ObserverPawn.Value?.ObserverServices is { } observer &&
                    observer.ObserverMode==(byte)ObserverMode_t.OBS_MODE_IN_EYE && observer.ObserverTarget.Raw==carrier.EntityHandle.Raw)
                    inEyeViewers.Add(player.EntityHandle.Raw);
        var hiddenIndices=fullyHidden ? attached.Select(e=>e.Index).Concat(cosmeticIndices).Distinct().ToArray() : [];
        Volatile.Write(ref _concealed,new(owner.EntityHandle.Raw,hiddenIndices,cosmeticIndices.ToArray(),inEyeIndices.ToArray(),inEyeViewers.ToArray()));
    }

    private static bool HasCosmeticDisplay(CBasePlayerWeapon weapon)
    {
        if (CoreConfig.FollowCS2ServerGuidelines) return false;
        var item=weapon.AttributeManager.Item;
        // The engine can create the nameplate and counter on the client without a server-side child.
        const ushort killEaterAttribute=80;
        return weapon.FallbackStatTrak>=0 || !string.IsNullOrEmpty(item.CustomName) ||
            !string.IsNullOrEmpty(item.CustomNameOverride) || !string.IsNullOrEmpty(item.CustomNameOverride2) ||
            !string.IsNullOrEmpty(item.CustomNameOverride3) ||
            item.AttributeList.Attributes.Any(attribute=>attribute.AttributeDefinitionIndex==killEaterAttribute) ||
            item.NetworkedDynamicAttributes.Attributes.Any(attribute=>attribute.AttributeDefinitionIndex==killEaterAttribute);
    }

    private static void AddCosmeticAttachments(CGameSceneNode? child,HashSet<uint> indices,int depth)
    {
        if (depth>=8) return;
        for (var count=0;child is not null && count<64;child=child.NextSibling,count++)
        {
            if (child.Owner is not { IsValid:true } entity) continue;
            var name=entity.DesignerName;
            if (name is "planted_c4" or "player" || name.Contains("viewmodel",StringComparison.Ordinal)) continue;
            if (IsCosmeticAttachment(name)) indices.Add(entity.Index);
            AddCosmeticAttachments(child.Child,indices,depth+1);
        }
    }

    private static bool IsCosmeticAttachment(string name) =>
        IsEquipmentModel(name) || name is "keychain_module" or "info_particle_system" or
        "env_particle_glow" or "env_sprite" or "env_glow" or "light_dynamic" ||
        name.StartsWith("light_",StringComparison.Ordinal) || name.StartsWith("prop_",StringComparison.Ordinal);

#if SERVER_SMOKE
    internal bool SuppressesCosmeticFor(uint viewer,uint entityIndex) =>
        Volatile.Read(ref _concealed) is { } hidden && hidden.SuppressedFor(viewer).Contains(entityIndex);
#endif
    public void SuppressGloves(CCSPlayerPawn pawn)
    {
        var raw=pawn.EntityHandle.Raw;
        if (_hiddenGloves is { } hidden && hidden.Pawn!=raw) RestoreGloves();
        var item=pawn.EconGloves;
        var definition=item.ItemDefinitionIndex;
        if (definition==0) return;

        // The client builds this wearable from the pawn's item view. Keep the paint and item ID intact.
        SetGloveDefinition(pawn,0);
        _hiddenGloves=new(raw,item.ItemID,definition,pawn.EconGlovesChanged);
    }

    private static void SetGloveDefinition(CCSPlayerPawn pawn,ushort definition)
    {
        pawn.EconGloves.ItemDefinitionIndex=definition;
        unchecked { pawn.EconGlovesChanged++; }
        Utilities.SetStateChanged(pawn,"CCSPlayerPawn","m_EconGloves");
        Utilities.SetStateChanged(pawn,"CCSPlayerPawn","m_nEconGlovesChanged");
    }

    private void RestoreGloves()
    {
        var hidden=_hiddenGloves;
        _hiddenGloves=null;
        if (hidden is null || Players.Entity<CCSPlayerPawn>(hidden.Pawn) is not { IsValid:true } pawn) return;
        var item=pawn.EconGloves;
        if (item.ItemDefinitionIndex==0 && item.ItemID==hidden.ItemId && pawn.EconGlovesChanged==hidden.Revision)
            SetGloveDefinition(pawn,hidden.Definition);
    }

    public void ClearConcealment()
    {
        Volatile.Write(ref _concealed,null);
        RestoreGloves();
    }

    public void Speed(CCSPlayerPawn pawn, string source, float amount)
    {
        var key=pawn.EntityHandle.Raw;
        // CS2 reduces this value temporarily when the player is tagged by a hit.
        // Start persistent boosts from at least normal speed so they cannot lock in that slowdown.
        if (!_scalars.TryGetValue(key,out var scalar)) _scalars[key]=scalar=new(Math.Max(1f,pawn.VelocityModifier));
        scalar.Multipliers[source]=amount;
        WriteSpeed(pawn,scalar.Baseline*scalar.Multiplier);
    }
    private static void WriteSpeed(CCSPlayerPawn? pawn,float amount)
    {
        if (pawn is not { IsValid:true }) return;
        pawn.VelocityModifier=amount;
        Utilities.SetStateChanged(pawn,"CCSPlayerPawn","m_flVelocityModifier");
    }
    public void RemoveSource(string source)
    {
        foreach (var (key, scalar) in _scalars.ToArray())
        {
            if (!scalar.Multipliers.Remove(source)) continue;
            WriteSpeed(Players.Entity<CCSPlayerPawn>(key),scalar.Baseline*scalar.Multiplier);
            if (scalar.Multipliers.Count == 0) _scalars.Remove(key);
        }
    }
    public void Fade(CBaseModelEntity entity, byte alpha)
    {
        if (!entity.IsValid) return;
        var raw=entity.EntityHandle.Raw;
        _colors.TryAdd(raw,(entity.Render,entity.RenderMode,entity.ShadowStrength,entity.Effects&HiddenEffects));
        var original=_colors[raw];
        var mode=alpha==0 ? RenderMode_t.kRenderNone : RenderMode_t.kRenderTransAlpha;
        SetVisibilityEffects(entity,alpha==0 ? HiddenEffects : original.effects);
        var shadow=alpha/255f*original.shadow;
        if (entity.Render.A==alpha && entity.RenderMode==mode && entity.ShadowStrength==shadow) return;
        entity.RenderMode=mode;
        entity.Render=Color.FromArgb(alpha,entity.Render.R,entity.Render.G,entity.Render.B);
        entity.ShadowStrength=shadow;
        Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_clrRender");
        Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_nRenderMode");
        Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_flShadowStrength");
    }
    public void RestoreColorsExcept(ISet<uint> keep)
    {
        foreach (var (raw, old) in _colors.ToArray())
        {
            if (keep.Contains(raw)) continue;
            var entity = Players.Entity<CBaseModelEntity>(raw);
            if (entity is { IsValid: true })
            {
                entity.Render = old.color; entity.RenderMode = old.mode; entity.ShadowStrength=old.shadow;
                SetVisibilityEffects(entity,old.effects);
                Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_clrRender");
                Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_nRenderMode");
                Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_flShadowStrength");
            }
            _colors.Remove(raw);
        }
    }
    private static void SetVisibilityEffects(CBaseModelEntity entity,uint flags)
    {
        var effects=entity.Effects;
        var updated=(effects&~HiddenEffects)|flags;
        if (updated==effects) return;
        entity.Effects=updated;
        Utilities.SetStateChanged(entity,"CBaseEntity","m_fEffects");
    }
    public void Reveal(CCSPlayerPawn pawn, CCSPlayerController viewer)
    {
        var slot = viewer.Slot;
        if (slot is < 0 or > 63) return;
        var word = slot / 32; var bit = 1u << (slot % 32); var state = pawn.EntitySpottedState;
        if (!_radar.ContainsKey(pawn.EntityHandle.Raw))
        {
            if (!SharedRadar.TryGetValue(pawn.EntityHandle.Raw,out var baseline)) SharedRadar[pawn.EntityHandle.Raw]=baseline=new(state.Spotted);
            baseline.Owners.Add(this);
        }
        _radar.TryAdd(pawn.EntityHandle.Raw, (word, bit, (state.SpottedByMask[word] & bit) != 0, state.Spotted));
        state.SpottedByMask[word] |= bit; state.Spotted = true;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_entitySpottedState");
        EnsureOutline(pawn, viewer);
    }
    private void EnsureOutline(CCSPlayerPawn pawn, CCSPlayerController viewer)
    {
        var raw = pawn.EntityHandle.Raw;
        if (_outlines.TryGetValue(raw, out var old))
        {
            if (old.Viewer == viewer.EntityHandle.Raw && Players.Entity<CDynamicProp>(old.Relay) is { IsValid: true } &&
                Players.Entity<CDynamicProp>(old.Glow) is { IsValid: true }) return;
            RemoveOutline(raw);
        }
        var model = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName;
        if (string.IsNullOrEmpty(model)) return;
        CDynamicProp? relay = null, glow = null;
        try
        {
            relay = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            glow = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (relay is null || glow is null) return;
            relay.Spawnflags = 256; glow.Spawnflags = 256;
            relay.SetModel(model); glow.SetModel(model);
            relay.DispatchSpawn(); glow.DispatchSpawn();
            _outlines[raw] = new(raw, viewer.EntityHandle.Raw, relay.EntityHandle.Raw, glow.EntityHandle.Raw, relay.Index, glow.Index);
            var relayRaw = relay.EntityHandle.Raw;
            var glowRaw = glow.EntityHandle.Raw;
            // Wait for the props to leave the engine's staging list before changing network state.
            Server.NextFrame(() =>
            {
                if (!_outlines.TryGetValue(raw, out var current) || current.Relay != relayRaw) return;
                var live = Players.Entity<CCSPlayerPawn>(raw);
                var parent = Players.Entity<CDynamicProp>(relayRaw);
                var outline = Players.Entity<CDynamicProp>(glowRaw);
                if (live is not { IsValid: true, Health: > 0 } || parent is null || outline is null) { RemoveOutline(raw); return; }
                parent.Render = Color.Transparent; parent.RenderMode = RenderMode_t.kRenderNone;
                outline.Render = Color.FromArgb(1, 0, 0, 0);
                outline.Glow.GlowType = 3; outline.Glow.GlowTeam = -1;
                outline.Glow.GlowRange = 0; outline.Glow.GlowRangeMin = 0;
                outline.Glow.GlowColorOverride = Color.FromArgb(255, 255, 60, 30);
                outline.Glow.Glowing = true;
                parent.Collision.SolidType = SolidType_t.SOLID_NONE;
                outline.Collision.SolidType = SolidType_t.SOLID_NONE;
                parent.AcceptInput("FollowEntity", live, parent, "!activator");
                outline.AcceptInput("FollowEntity", parent, outline, "!activator");
                Utilities.SetStateChanged(parent, "CBaseModelEntity", "m_clrRender");
                Utilities.SetStateChanged(parent, "CBaseModelEntity", "m_nRenderMode");
                Utilities.SetStateChanged(outline, "CBaseModelEntity", "m_clrRender");
                Utilities.SetStateChanged(outline, "CBaseModelEntity", "m_Glow");
                Volatile.Write(ref _transmitOutlines, _outlines.Values.ToArray());
            });
        }
        finally
        {
            if (!_outlines.ContainsKey(raw))
            {
                if (glow is { IsValid: true }) glow.Remove();
                if (relay is { IsValid: true }) relay.Remove();
            }
        }
    }
    // CheckTransmit may run on networking workers. Only read an immutable index snapshot here.
    public void FilterTransmission(CCheckTransmitInfoList clients)
    {
        FilterOutlines(clients);
        var hidden=Volatile.Read(ref _concealed);
        if (hidden is null || hidden.CosmeticIndices.Length==0) return;
        // These weapons spawn third-person accessories on the client. The carrier still sees their weapon.
        foreach (var (info,viewer) in clients)
        {
            var indices=hidden.SuppressedFor(viewer?.EntityHandle.Raw??0);
            if (indices.Length==0) continue;
            // The optional always-send bit vector belongs to SourceTV and replay.
            var hasAlways=Unsafe.BitCast<CFixedBitVecBase,nint>(info.TransmitAlways)!=0;
            foreach (var index in indices)
            {
                info.TransmitEntities.Remove(index);
                if (hasAlways) info.TransmitAlways.Remove(index);
            }
        }
    }
    public void FilterOutlines(CCheckTransmitInfoList clients)
    {
        var outlines = Volatile.Read(ref _transmitOutlines);
        if (outlines.Length == 0) return;
        foreach (var (info, viewer) in clients)
        {
            var raw = viewer?.EntityHandle.Raw;
            foreach (var outline in outlines)
            {
                if (raw == outline.Viewer) { info.TransmitEntities.Add(outline.RelayIndex); info.TransmitEntities.Add(outline.GlowIndex); }
                else { info.TransmitEntities.Remove(outline.RelayIndex); info.TransmitEntities.Remove(outline.GlowIndex); }
            }
        }
    }
    // Invisibility uses model alpha rather than removing a live pawn/equipment from PVS.
    // Removing entities referenced by a client controller/viewmodel can crash the client.
    // Weapons with independently rendered cosmetics are filtered separately for other viewers.
    public static void SuppressRadar(CCSPlayerPawn pawn)
    {
        var state=pawn.EntitySpottedState;
        var spotted=state.Spotted;
        for (var i=0;i<state.SpottedByMask.Length;i++) spotted|=state.SpottedByMask[i]!=0;
        if (!spotted) return;
        state.Spotted=false;
        for (var i=0;i<state.SpottedByMask.Length;i++) state.SpottedByMask[i]=0;
        Utilities.SetStateChanged(pawn,"CCSPlayerPawn","m_entitySpottedState");
    }
    public void PruneOutlines(ISet<uint> living)
    { foreach (var target in _outlines.Keys.Where(raw => !living.Contains(raw)).ToArray()) RemoveOutline(target); }
    private void RemoveOutline(uint raw)
    {
        if (!_outlines.Remove(raw, out var outline)) return;
        Volatile.Write(ref _transmitOutlines, _outlines.Values.ToArray());
        Players.Entity<CDynamicProp>(outline.Glow)?.Remove();
        Players.Entity<CDynamicProp>(outline.Relay)?.Remove();
    }
    public void ClearRadar()
    {
        foreach (var raw in _outlines.Keys.ToArray()) RemoveOutline(raw);
        foreach (var (raw, old) in _radar)
        {
            var originalSpotted=old.spotted;
            if (SharedRadar.TryGetValue(raw,out var baseline))
            {
                originalSpotted=baseline.Spotted; baseline.Owners.Remove(this);
                if (baseline.Owners.Count==0) SharedRadar.Remove(raw);
            }
            var pawn = Players.Entity<CCSPlayerPawn>(raw); if (pawn is not { IsValid: true }) continue;
            var state = pawn.EntitySpottedState;
            if (!old.hadBit) state.SpottedByMask[old.word] &= ~old.bit;
            state.Spotted = originalSpotted || state.SpottedByMask[0] != 0 || state.SpottedByMask[1] != 0;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_entitySpottedState");
        }
        _radar.Clear();
    }
    public void Clear()
    {
        foreach (var (key, scalar) in _scalars) WriteSpeed(Players.Entity<CCSPlayerPawn>(key),scalar.Baseline);
        _scalars.Clear(); RestoreColorsExcept(new HashSet<uint>()); ClearRadar(); ClearConcealment();
    }
}
