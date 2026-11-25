using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using Microsoft.Extensions.Logging;
using Superpower1v5.Configuration;
using Superpower1v5.GameMode;
using Superpower1v5.Managers;
using Superpower1v5.Powers.Active;
using Superpower1v5.Powers.Combat;
using Superpower1v5.Support;

namespace Superpower1v5;

[MinimumApiVersion(380)]
public sealed partial class Plugin : BasePlugin,IPluginConfig<PluginConfig>
{
    public override string ModuleName => "Superpower 1v5";
    public override string ModuleVersion => "1.9.2";
    public override string ModuleAuthor => "Superpower1v5";
    public override string ModuleDescription => "The chosen T player has two private powers against up to five CT opponents.";
    public const string Prefix = " \x04[SUPERPOWER]\x01 ";
    public PluginConfig Config { get; set; } = new();
    public ModeController Mode { get; private set; } = null!;
    public PowerManager Powers { get; private set; } = null!;
    public EffectsManager Effects => Powers.GetEffects(Mode.Boss);
    public DamageService Damage { get; private set; } = null!;
    public FeedbackManager Feedback { get; private set; } = null!;
    internal Dictionary<uint,Action> CloneHits { get; }=new();
    private TinyFiringOrigin? _tinyFiringOrigin;
    private bool _unloaded;
    private double _slowAt, _maintenanceAt;
    private readonly HashSet<uint> _entities=[];
    private readonly HashSet<uint> _noCluster=[];
    public void SuppressCluster(uint raw) => _noCluster.Add(raw);

    public void OnConfigParsed(PluginConfig config) { config.Validate(); Config=config; }
    public override void Load(bool hotReload)
    {
        Config.Validate(); Powers=new(this); Mode=new(this); Damage=new(this); Feedback=new(this);
        RegisterEvents();
        _tinyFiringOrigin=new(this);
        _tinyFiringOrigin.Install();
        RegisterListener<Listeners.OnTick>(()=>Guard(Tick));
        RegisterListener<Listeners.OnServerPostEntityThink>(()=>Guard(Powers.PostThink));
        RegisterListener<Listeners.OnMapStart>(_=>Guard(()=> { Mode.MapEnd(); Mode.EnforceCvars(); }));
        RegisterListener<Listeners.OnMapEnd>(()=>Guard(()=> { Mode.MapEnd(); ClearEntities(); }));
        RegisterListener<Listeners.OnClientDisconnect>(slot=>Guard(()=> {
            var player=Utilities.GetPlayerFromSlot(slot); Powers.Stop(player);
            if (Mode.Boss?.Slot==slot) Mode.BossLeft();
        }));
        RegisterListener<Listeners.OnEntityTakeDamagePre>((entity,info)=> {
            try
            {
                if (entity is null || info is null || entity.Handle==IntPtr.Zero || info.Handle==IntPtr.Zero) return HookResult.Continue;
                if (!Damage.Synthetic && Mode.Live && Config.Enabled && ThunderGod.IsNativeTaserDamage(info) &&
                    Players.Controller(info.Attacker.Value) is { } shooter && Powers.GetActive(shooter).Any(p=>p is ThunderGod) &&
                    Mode.CanUsePlayerPowers(shooter))
                {
                    info.Damage=0; info.OriginalDamage=0; info.TotalledDamage=0;
                    return HookResult.Handled;
                }
                if (CloneHits.TryGetValue(entity.EntityHandle.Raw,out var shot))
                {
                    if (info.Damage>0 && (info.BitsDamageType & (DamageTypes_t.DMG_BULLET|DamageTypes_t.DMG_BUCKSHOT))!=0) shot();
                    return HookResult.Handled;
                }
                if (Damage.Synthetic && entity.EntityHandle.Raw!=Damage.TargetPawn) return HookResult.Handled;
                return HookResult.Continue;
            }
            catch (Exception e) { Fail(e); return HookResult.Continue; }
        });
        RegisterListener<Listeners.OnPlayerTakeDamagePre>((pawn,info)=> {
            try { return pawn is { IsValid:true } && info is not null && info.Handle!=IntPtr.Zero ? BeforeDamage(pawn,info) : HookResult.Continue; }
            catch (Exception e) { Fail(e); return HookResult.Continue; }
        });
        AddCommandListener("jointeam",(p,cmd)=> {
            if (Config.Enabled && p is { IsValid:true } && cmd.ArgCount>1 && cmd.GetArg(1)=="2" && !Mode.CanJoinT(p))
            { p.PrintToChat(Prefix+"T already has a boss. Join CT until the T slot is free."); return HookResult.Handled; }
            return HookResult.Continue;
        },HookMode.Pre);
        RegisterListener<Listeners.OnEntitySpawned>(entity=> {
            if (entity is not { IsValid:true } || !GrenadeFactory.Supported(entity.DesignerName)) return;
            var raw=entity.EntityHandle.Raw;
            Defer(()=> { if (!_noCluster.Contains(raw) && Players.Entity<CBaseCSGrenadeProjectile>(raw) is { IsValid:true } projectile) Powers.Projectile(projectile); });
        });
        RegisterListener<Listeners.OnPlayerTakeDamagePost>((p,info,result)=>Guard(()=>AfterDamage(p,info,result)));
        RegisterListener<Listeners.CheckTransmit>(clients=>Powers.FilterTransmission(clients));
        Server.NextWorldUpdate(()=>Guard(()=> { Mode.EnforceCvars(); if (hotReload) Mode.QueueRestart(); }));
        Logger.LogInformation("Superpower1v5 {Version} loaded. API 380; {PowerCount} powers; hot reload={HotReload}",ModuleVersion,Models.PowerCatalog.All.Count,hotReload);
#if SERVER_SMOKE
        BeginNativeProbe();
#endif
    }
    private void Tick()
    {
        if (!Config.Enabled) return;
        Powers.Tick();
        if (Server.CurrentTime>=_slowAt) { _slowAt=Server.CurrentTime+.1; Powers.SlowTick(); Feedback.Hud(); }
        if (Server.CurrentTime>=_maintenanceAt) { _maintenanceAt=Server.CurrentTime+1; Mode.Maintenance(); }
    }
    public void Broadcast(string text)
    {
#if SERVER_SMOKE
        _probeBroadcasts.Add(text);
#endif
        Server.PrintToChatAll(Prefix+text);
    }
    public void Defer(Action action)
    {
        var generation=Mode.Generation;
        Server.NextFrame(()=> { if (!_unloaded && Mode.Generation==generation && Mode.Live) Guard(action); });
    }
    public void Guard(Action action) { if (_unloaded) return; try { action(); } catch (Exception e) { Fail(e); } }
    private void Fail(Exception e)
    {
        Logger.LogError(e,"Superpower1v5 disabled after a runtime error; use css_sp_debug and inspect the log.");
        Config.Enabled=false;
        try { Mode.ResetRound(); ClearEntities(); Mode.RestoreCvars(); }
        catch (Exception cleanup) { Logger.LogError(cleanup,"Cleanup failed"); }
    }
    public void TrackEntity(uint raw) => _entities.Add(raw);
    public int TrackedEntityCount => _entities.Count;
    public void RemoveTrackedEntity(uint raw)
    {
        _noCluster.Remove(raw);
        if (!_entities.Remove(raw)) return;
        var entity=new CEntityInstance(raw); if (entity.IsValid) entity.Remove();
    }
    public void ClearEntities() { foreach (var raw in _entities.ToArray()) RemoveTrackedEntity(raw); _noCluster.Clear(); }
    public override void Unload(bool hotReload)
    {
        _tinyFiringOrigin?.Clear();
        Mode.ResetRound(); ClearEntities(); Mode.RestoreCvars(); _unloaded=true;
        Logger.LogInformation("Superpower1v5 unloaded; temporary effects and server ConVars restored.");
    }
}
