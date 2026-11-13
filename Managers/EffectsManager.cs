using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Superpower1v5.Support;

namespace Superpower1v5.Managers;

public sealed class EffectsManager
{
    private sealed class Scalar(float baseline, float actualBaseline)
    {
        public float Baseline { get; } = baseline;
        public float ActualBaseline { get; } = actualBaseline;
        public Dictionary<string, float> Multipliers { get; } = new();
        public float Multiplier => Multipliers.Values.Aggregate(1f, (a, b) => a * b);
    }
    private sealed record Outline(uint Target, uint Viewer, uint Relay, uint Glow, uint RelayIndex, uint GlowIndex);
    private readonly Dictionary<(uint pawn, bool gravity), Scalar> _scalars = new();
    private readonly Dictionary<uint, (Color color, RenderMode_t mode)> _colors = new();
    private readonly Dictionary<uint, (int word, uint bit, bool hadBit, bool spotted)> _radar = new();
    private readonly Dictionary<uint, Outline> _outlines = new();
    private Outline[] _transmitOutlines = [];
    private sealed record Concealed(uint Viewer,uint[] Indices);
    private Concealed? _concealed;
    public int OutlineCount => _outlines.Count;
    public int HiddenEntityCount => Volatile.Read(ref _concealed)?.Indices.Length ?? 0;
    public bool HiddenFrom(uint viewer,uint entityIndex) => Volatile.Read(ref _concealed) is { } hidden && hidden.Viewer!=viewer && Array.IndexOf(hidden.Indices,entityIndex)>=0;
    public void Conceal(CCSPlayerPawn pawn,CCSPlayerController viewer)
    {
        var owned=new Dictionary<uint,uint> { [pawn.EntityHandle.Raw]=pawn.Index };
        var entities=Utilities.GetAllEntities().Where(e=>e.IsValid).ToArray();
        // Follow ownership and parenting through equipment and weapon world models.
        for (var pass=0;pass<16;pass++)
        {
            var added=false;
            foreach (var entity in entities)
            {
                if (!entity.IsValid || owned.ContainsKey(entity.EntityHandle.Raw) || entity.DesignerName.EndsWith("_projectile",StringComparison.Ordinal) || entity.DesignerName=="inferno") continue;
                var model=entity.As<CBaseEntity>();
                var parent=model.CBodyComponent?.SceneNode?.PParent?.Owner;
                if (!owned.ContainsKey(model.OwnerEntity.Raw) && (parent is null || !owned.ContainsKey(parent.EntityHandle.Raw))) continue;
                owned[entity.EntityHandle.Raw]=entity.Index; added=true;
            }
            if (!added) break;
        }
        Volatile.Write(ref _concealed,new(viewer.EntityHandle.Raw,owned.Values.ToArray()));
    }
    public void ClearConcealment() => Volatile.Write(ref _concealed,null);

    public void Speed(CCSPlayerPawn pawn, string source, float amount) => SetScalar(pawn, source, amount, false);
    public void Gravity(CCSPlayerPawn pawn, string source, float amount) => SetScalar(pawn, source, amount, true);
    private void SetScalar(CCSPlayerPawn pawn, string source, float amount, bool gravity)
    {
        var key = (pawn.EntityHandle.Raw, gravity);
        if (!_scalars.TryGetValue(key, out var scalar))
            _scalars[key] = scalar = new(gravity ? pawn.GravityScale : pawn.VelocityModifier, gravity ? pawn.ActualGravityScale : 0);
        scalar.Multipliers[source] = amount;
        Write(pawn, gravity, scalar.Baseline * scalar.Multiplier, scalar.ActualBaseline * scalar.Multiplier);
    }
    private static void Write(CCSPlayerPawn? pawn, bool gravity, float amount, float actualAmount)
    {
        if (pawn is not { IsValid: true }) return;
        if (gravity)
        {
            pawn.GravityScale = amount;
            pawn.ActualGravityScale = actualAmount;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_flGravityScale");
            // ActualGravityScale is server-only and must not be passed to SetStateChanged.
        }
        else
        {
            pawn.VelocityModifier = amount;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
        }
    }
    public void RemoveSource(string source)
    {
        foreach (var (key, scalar) in _scalars.ToArray())
        {
            if (!scalar.Multipliers.Remove(source)) continue;
            Write(Players.Entity<CCSPlayerPawn>(key.pawn), key.gravity, scalar.Baseline * scalar.Multiplier, scalar.ActualBaseline * scalar.Multiplier);
            if (scalar.Multipliers.Count == 0) _scalars.Remove(key);
        }
    }
    public void Fade(CBaseModelEntity entity, byte alpha)
    {
        if (!entity.IsValid) return;
        _colors.TryAdd(entity.EntityHandle.Raw, (entity.Render, entity.RenderMode));
        entity.RenderMode = RenderMode_t.kRenderTransAlpha;
        entity.Render = Color.FromArgb(alpha, entity.Render.R, entity.Render.G, entity.Render.B);
        Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_clrRender");
        Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_nRenderMode");
    }
    public void RestoreColorsExcept(ISet<uint> keep)
    {
        foreach (var (raw, old) in _colors.ToArray())
        {
            if (keep.Contains(raw)) continue;
            var entity = Players.Entity<CBaseModelEntity>(raw);
            if (entity is { IsValid: true })
            {
                entity.Render = old.color; entity.RenderMode = old.mode;
                Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_clrRender");
                Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_nRenderMode");
            }
            _colors.Remove(raw);
        }
    }
    public void Reveal(CCSPlayerPawn pawn, CCSPlayerController viewer)
    {
        var slot = viewer.Slot;
        if (slot is < 0 or > 63) return;
        var word = slot / 32; var bit = 1u << (slot % 32); var state = pawn.EntitySpottedState;
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
        var outlines = Volatile.Read(ref _transmitOutlines);
        var hidden=Volatile.Read(ref _concealed);
        if (outlines.Length == 0 && hidden is null) return;
        foreach (var (info, viewer) in clients)
        {
            var raw = viewer?.EntityHandle.Raw;
            foreach (var outline in outlines)
            {
                if (raw == outline.Viewer) { info.TransmitEntities.Add(outline.RelayIndex); info.TransmitEntities.Add(outline.GlowIndex); }
                else { info.TransmitEntities.Remove(outline.RelayIndex); info.TransmitEntities.Remove(outline.GlowIndex); }
            }
            if (hidden is not null && raw!=hidden.Viewer)
                foreach (var index in hidden.Indices) info.TransmitEntities.Remove(index);
        }
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
            var pawn = Players.Entity<CCSPlayerPawn>(raw); if (pawn is not { IsValid: true }) continue;
            var state = pawn.EntitySpottedState;
            if (!old.hadBit) state.SpottedByMask[old.word] &= ~old.bit;
            state.Spotted = old.spotted || state.SpottedByMask[0] != 0 || state.SpottedByMask[1] != 0;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_entitySpottedState");
        }
        _radar.Clear();
    }
    public void Clear()
    {
        foreach (var (key, scalar) in _scalars) Write(Players.Entity<CCSPlayerPawn>(key.pawn), key.gravity, scalar.Baseline, scalar.ActualBaseline);
        _scalars.Clear(); RestoreColorsExcept(new HashSet<uint>()); ClearRadar(); ClearConcealment();
    }
}
