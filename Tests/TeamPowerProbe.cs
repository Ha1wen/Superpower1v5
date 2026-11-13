#if SERVER_SMOKE
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Superpower1v5.Powers.Combat;
using Superpower1v5.Support;

namespace Superpower1v5;

public sealed partial class Plugin
{
    private string? _fixedBoss;
    private Vector? _rewindStart;
    private readonly Dictionary<string,int> _utilityEvents=new();
    private bool _realShotWaiting,_realShotSaved;
    private int _realShots;
    private static void StopProbeBots(bool stop) { if (ConVar<int>.Find("bot_stop") is { } variable) variable.ValueAsString=stop ? "1" : "0"; }
    private void CountUtility(string key) => _utilityEvents[key]=_utilityEvents.GetValueOrDefault(key)+1;
    private void ScheduleTeamPowerTests()
    {
        foreach (var entry in Config.Powers) entry.Value.Enabled=entry.Key is "teleport" or "infinite_ammo";
        StopProbeBots(true);
        Config.InvisibilityQuietDelay=0; Config.InvisibilityFadeDuration=.1f;
        RegisterEventHandler<EventHegrenadeDetonate>((e,i)=> { CountUtility("he"); return HookResult.Continue; });
        RegisterEventHandler<EventFlashbangDetonate>((e,i)=> { CountUtility("flash"); return HookResult.Continue; });
        RegisterEventHandler<EventSmokegrenadeDetonate>((e,i)=> { CountUtility("smoke"); return HookResult.Continue; });
        RegisterEventHandler<EventMolotovDetonate>((e,i)=> { CountUtility("fire"); return HookResult.Continue; });
        RegisterEventHandler<EventDecoyStarted>((e,i)=> { CountUtility("decoy"); return HookResult.Continue; });
        RegisterEventHandler<EventWeaponFire>((e,i)=> { if (_realShotWaiting) _realShots++; return HookResult.Continue; });
        RegisterListener<Listeners.OnPlayerTakeDamagePre>((pawn,info)=> {
            if (_realShotWaiting && Mode.IsBoss(Players.Controller(pawn))) Logger.LogInformation("REAL BULLET HP={Health}, damage={Damage}, total={Total}, original={Original}, type={Type}, hit={Hit}, consumed={Used}",pawn.Health,info.Damage,info.TotalledDamage,info.OriginalDamage,info.BitsDamageType,info.HitGroupId,Powers.Loadout?.Consumed.Contains("second_chance"));
            return HookResult.Continue;
        });
        RegisterListener<Listeners.OnTick>(()=> {
            if (!_realShotWaiting || Powers.Loadout?.Consumed.Contains("second_chance")!=true) return;
            _realShotWaiting=false; _realShotSaved=true; StopProbeBots(true);
            Probe("Second Chance intercepts an actual bot weapon hit",()=>Require(Players.AlivePawn(Mode.Boss) is { Health:>0 },"boss died to real weapon damage"));
        });
        AddTimer(12,()=>Probe("One T boss and five CT players; all 18 powers apply",()=> {
            Require(Mode.Live && Mode.Boss?.TeamNum==2 && Mode.Opponents.Count()==5,"roster");
            _fixedBoss=Players.Key(Mode.Boss!);
            foreach (var definition in Models.PowerCatalog.All)
            { Powers.NativeProbePair(definition.Id,definition.Id=="infinite_ammo" ? "teleport" : "infinite_ammo"); Powers.Tick(); Powers.SlowTick(); }
            Powers.NativeProbePair("invisibility","infinite_ammo");
            AddTimer(.35f,()=>Probe("Quiet invisibility conceals body and refreshed equipment for opponents only",()=> {
            var boss=Mode.Boss!; var pawn=Players.AlivePawn(boss)!; var opponent=Mode.Opponents.First();
            Require(Effects.HiddenFrom(opponent.EntityHandle.Raw,pawn.Index) && !Effects.HiddenFrom(boss.EntityHandle.Raw,pawn.Index),"body recipient filtering");
            Require(Effects.HiddenEntityCount>=2,"owned weapon missing from concealment");
            var attachment=Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic")!;
            attachment.SetModel(pawn.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName); attachment.DispatchSpawn(); attachment.OwnerEntity.Raw=pawn.EntityHandle.Raw;
            Powers.SlowTick(); Require(Effects.HiddenFrom(opponent.EntityHandle.Raw,attachment.Index),"equipment visible");
            attachment.OwnerEntity.Raw=uint.MaxValue; Powers.SlowTick(); Require(!Effects.HiddenFrom(opponent.EntityHandle.Raw,attachment.Index),"detached item remains hidden"); attachment.Remove();
            Powers.NativeProbePair("teleport","infinite_ammo"); Require(Effects.HiddenEntityCount==0,"concealment leaked");
            }),TimerFlags.STOP_ON_MAPCHANGE);
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(13,()=>Probe("Juggernaut has 500 HP, takes damage, heals normally and restores 100 HP on removal",()=> {
            Powers.NativeProbePair("juggernaut","infinite_ammo"); var boss=Mode.Boss!; var pawn=Players.AlivePawn(boss)!;
            Require(pawn.Health==500 && pawn.MaxHealth==500,"500 HP not applied");
            Damage.NativeProbeHit(boss.EntityHandle.Raw,Mode.Opponents.First().EntityHandle.Raw,100,DamageTypes_t.DMG_GENERIC);
            Require(pawn.Health==400,"juggernaut damage"); Powers.Tick(); Require(pawn.Health==400,"juggernaut healed every tick");
            Players.Heal(boss,50); Require(pawn.Health==450,"500 HP healing cap");
            Mode.Spawn(boss); Require(pawn.Health==500,"spawn reset overwrote juggernaut");
            Powers.NativeProbePair("teleport","infinite_ammo"); Require(pawn.MaxHealth==100 && pawn.Health==100,"juggernaut cleanup");
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(14,()=>Probe("Second Chance records its rewind history",()=> {
            Powers.NativeProbePair("second_chance","infinite_ammo"); _rewindStart=Players.Copy(Players.AlivePawn(Mode.Boss)!.AbsOrigin!);
            Require(((SecondChance)Powers.Active[0]).SnapshotCount>0,"no initial history");
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(17.3f,()=>Probe("Lethal native damage rewinds position and restores health without a death",()=> {
            var boss=Mode.Boss!; var pawn=Players.AlivePawn(boss)!; var start=_rewindStart!;
            pawn.Teleport(new Vector(start.X,start.Y,start.Z+80),null,new Vector(0,0,0)); pawn.Health=20;
            Damage.NativeProbeHit(boss.EntityHandle.Raw,Mode.Opponents.First().EntityHandle.Raw,500,DamageTypes_t.DMG_BULLET);
            Require(Players.AlivePawn(boss)?.Health==100 && Powers.Loadout!.Consumed.Contains("second_chance"),"lethal hit not prevented");
            Require(Players.Distance(pawn.AbsOrigin!,start)<40,$"position was not rewound: frame={((SecondChance)Powers.Active[0]).LastRestoredFrame}, actual={pawn.AbsOrigin}, expected={start}");
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(18.2f,()=>Probe("Second Chance only protects once per round",()=> {
            var boss=Mode.Boss!; Damage.NativeProbeHit(boss.EntityHandle.Raw,Mode.Opponents.First().EntityHandle.Raw,500,DamageTypes_t.DMG_GENERIC);
            Require(Players.AlivePawn(boss) is null,"second lethal hit survived"); Server.ExecuteCommand("mp_restartgame 1");
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(24,()=>Probe("The same T player stays boss after a round restart",()=> {
            Require(Mode.Live && Players.Key(Mode.Boss!)==_fixedBoss && Mode.Boss!.TeamNum==2 && Mode.Opponents.Count()==5,"boss rotated or teams changed");
            foreach (var player in Mode.Eligible()) { var pawn=Players.AlivePawn(player)!; pawn.Health=10000; pawn.MaxHealth=10000; }
        }),TimerFlags.STOP_ON_MAPCHANGE);
        var utilities=new[] { ("hegrenade_projectile","he",(ushort)44),("flashbang_projectile","flash",(ushort)43),("smokegrenade_projectile","smoke",(ushort)45),
            ("molotov_projectile","fire",(ushort)46),("molotov_projectile","fire",(ushort)48),("decoy_projectile","decoy",(ushort)47) };
        for (var index=0;index<utilities.Length;index++)
        {
            var (kind,key,item)=utilities[index]; var at=26+index*6;
            AddTimer(at,()=>StartUtilityProbe(kind,key,item),TimerFlags.STOP_ON_MAPCHANGE);
        }
        AddTimer(63,()=> { Powers.NativeProbePair("teleport","infinite_ammo"); Server.ExecuteCommand("mp_restartgame 1"); },TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(68,()=>Probe("Real weapon rewind scenario prepares",()=> {
            Powers.NativeProbePair("second_chance","infinite_ammo");
            var boss=Mode.Boss!; var pawn=Players.AlivePawn(boss)!; _rewindStart=Players.Copy(pawn.AbsOrigin!);
            pawn.Health=100; pawn.ArmorValue=0;
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(71.2f,()=> {
            if (Players.AlivePawn(Mode.Boss) is not { } boss || boss.AbsOrigin is not { } position) return;
            var shooter=Mode.Opponents.First(); var enemy=Players.AlivePawn(shooter)!;
            Mode.Boss!.RemoveWeapons();
            boss.MoveType=MoveType_t.MOVETYPE_NONE; boss.ActualMoveType=MoveType_t.MOVETYPE_NONE; boss.Health=1;
            enemy.Teleport(new(position.X+160,position.Y,position.Z),new QAngle(0,180,0),new Vector(0,0,0)); enemy.EyeAngles.Y=180;
            var gun=shooter.GiveNamedItem<CBasePlayerWeapon>("weapon_ak47"); if (gun is not null) enemy.WeaponServices!.ActiveWeapon.Raw=gun.EntityHandle.Raw;
            if (enemy.Bot is { } bot) { bot.LookAtSpot.X=position.X; bot.LookAtSpot.Y=position.Y; bot.LookAtSpot.Z=position.Z+60; bot.LookAtSpotDuration=20; bot.LookAtSpotTimestamp=Server.CurrentTime; bot.LookAtSpotAttack=true; }
            Logger.LogInformation("REAL SHOT SCENARIO boss={Boss}, shooter={Shooter}, visible={Visible}",position,enemy.AbsOrigin,SafeTeleport.Visible(new(position.X,position.Y,position.Z+60),new(enemy.AbsOrigin!.X,enemy.AbsOrigin.Y,enemy.AbsOrigin.Z+60),boss));
            _realShotWaiting=true; if (ConVar<int>.Find("bot_dont_shoot") is { } noShoot) noShoot.ValueAsString="0"; StopProbeBots(false);
        },TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(79,()=> {
            Probe("Real weapon test completed and plugin stayed enabled",()=>Require(_realShotSaved && Config.Enabled,$"bot did not fire a protected lethal shot: fires={_realShots}, bossAlive={Players.AlivePawn(Mode.Boss) is not null}, consumed={Powers.Loadout?.Consumed.Contains("second_chance")}"));
            StopProbeBots(true); Server.ExecuteCommand("mp_restartgame 1");
        },TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(85,()=> {
            Probe("Final round has one permanent T boss, two powers and normal health",()=>Require(Config.Enabled && Mode.Live && Players.Key(Mode.Boss!)==_fixedBoss && Mode.Opponents.Count()==5 && Mode.Eligible().All(p=>Players.AlivePawn(p)?.Health==100),"final round"));
            File.WriteAllText(Path.Combine(ProbeDirectory,"native-probe-results.json"),JsonSerializer.Serialize(_probeResults,new JsonSerializerOptions { WriteIndented=true }));
            Logger.LogInformation("TEAM POWERS PROBE finished; unloading private test plugin.");
            Server.NextWorldUpdate(static ()=>Server.ExecuteCommand("quit")); Server.ExecuteCommand("css_plugins unload \"Superpower 1v5\"");
        },TimerFlags.STOP_ON_MAPCHANGE);
    }
    private void StartUtilityProbe(string kind,string key,ushort item)
    {
        Probe($"Cluster creates four {kind} projectiles (item {item})",()=> {
            Powers.NativeProbePair("cluster_grenade","infinite_ammo");
            var owner=Players.AlivePawn(Mode.Boss)!; var feet=owner.AbsOrigin!;
            var before=_utilityEvents.GetValueOrDefault(key);
            var original=GrenadeFactory.Create(kind,new(feet.X,feet.Y,feet.Z+80),new(200,0,100),owner,item);
            Require(original is { IsValid:true },"native projectile creation failed"); var raw=original!.EntityHandle.Raw;
            AddTimer(.25f,()=>Probe($"Four {kind} copies do not split recursively",()=> {
                var count=Utilities.FindAllEntitiesByDesignerName<CBaseCSGrenadeProjectile>(kind).Count(p=>p.Thrower.Raw==owner.EntityHandle.Raw);
                Require(count==4 && ((ClusterGrenade)Powers.Active[0]).ChildCount==3,$"projectiles={count}, children={((ClusterGrenade)Powers.Active[0]).ChildCount}");
            }),TimerFlags.STOP_ON_MAPCHANGE);
            AddTimer(5.5f,()=>Probe($"All four {kind} utilities actually activate (item {item})",()=> {
                var count=_utilityEvents.GetValueOrDefault(key)-before;
                try { Require(count>=4,$"native activation events={count}"); }
                finally { Powers.NativeProbePair("teleport","infinite_ammo"); if (Players.Entity<CBaseCSGrenadeProjectile>(raw) is { IsValid:true } entity) entity.Remove(); }
            }),TimerFlags.STOP_ON_MAPCHANGE);
        });
    }
}
#endif
