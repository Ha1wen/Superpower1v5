#if SERVER_SMOKE
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Superpower1v5.Models;
using Superpower1v5.Support;

namespace Superpower1v5;

public sealed partial class Plugin
{
    private readonly List<object> _probeResults=[];
    private bool _probeStarted;
    private string? _probeFirstBoss;
    private string ProbeDirectory => Environment.GetEnvironmentVariable("SUPERPOWER1V5_PROBE_ARTIFACTS") ??
        Path.GetFullPath(Path.Combine(ModuleDirectory,"..","..","..","..","..","..","development","Superpower1v5","artifacts"));
    private void BeginNativeProbe()
    {
        var authorization=Path.Combine(ProbeDirectory,"authorized-smoke-pid.txt");
        if (!File.Exists(authorization) || File.ReadAllText(authorization).Trim()!=Environment.ProcessId.ToString()) return;
        RegisterListener<Listeners.OnMapStart>(_=>Server.NextWorldUpdate(StartNativeProbe));
        if (!string.IsNullOrEmpty(Server.MapName)) Server.NextWorldUpdate(StartNativeProbe);
    }
    private void StartNativeProbe()
    {
        if (_probeStarted) return;
        _probeStarted=true;
        Config.Powers["juggernaut"].Enabled=false;
        Config.Powers["cluster_grenade"].Enabled=false;
        Logger.LogInformation("FEEDBACK PROBE authorized for isolated PID {Pid}; module={Module}",Environment.ProcessId,ModuleDirectory);
        Server.NextWorldUpdate(()=> {
            Server.ExecuteCommand("sv_hibernate_when_empty 0"); Server.ExecuteCommand("bot_join_after_player 0");
            Server.ExecuteCommand("bot_quota_mode normal"); Server.ExecuteCommand("bot_quota 6"); Server.ExecuteCommand("bot_stop 1");
            Server.ExecuteCommand("mp_freezetime 1"); Server.ExecuteCommand("mp_warmup_end"); Server.ExecuteCommand("mp_restartgame 1");
        });
        if (ProbeDirectory.Contains("new-powers-native",StringComparison.OrdinalIgnoreCase))
        { ScheduleNewPowerTests(); return; }
        if (ProbeDirectory.Contains("persistent-native",StringComparison.OrdinalIgnoreCase))
        { SchedulePersistentPowerTests(); return; }
        if (ProbeDirectory.Contains("random-kill-native",StringComparison.OrdinalIgnoreCase))
        { ScheduleRandomKillTests(); return; }
        if (ProbeDirectory.Contains("team-powers-native",StringComparison.OrdinalIgnoreCase))
        { ScheduleTeamPowerTests(); return; }
        if (ProbeDirectory.Contains("rewind-gun-native",StringComparison.OrdinalIgnoreCase))
        { ScheduleGunRewindProbe(); return; }
        AddTimer(12,NativeFeedbackProbe,TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(13,GravityPhysicsProbe,TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(16,()=> {
            Probe("Second Chance prevents a real lethal hit and records consumption",()=> {
                var boss=Mode.Boss!; Players.Heal(boss,100); Powers.NativeProbePair("second_chance","infinite_ammo");
                Damage.NativeProbeHit(boss.EntityHandle.Raw,Mode.Opponents.First().EntityHandle.Raw,200,DamageTypes_t.DMG_GENERIC);
                Require(Players.AlivePawn(boss)?.Health==100 && Powers.Loadout!.Consumed.Contains("second_chance"),"lethal protection failed");
            });
            Server.ExecuteCommand("mp_restartgame 1");
        },TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(21,()=> {
            Probe("Next round retains the chosen T boss and the 1v5/two-power invariants",()=> {
                Require(Mode.Live && Powers.Loadout?.Powers.Count==2 && Mode.Opponents.Count()==5,"invalid round state");
                Require(Players.Key(Mode.Boss!)==_probeFirstBoss,"chosen T boss changed");
                Require(Mode.Eligible().All(p=>Players.AlivePawn(p)?.Health==100),"spawn health");
            });
            File.WriteAllText(Path.Combine(ProbeDirectory,"native-probe-results.json"),JsonSerializer.Serialize(_probeResults,new JsonSerializerOptions { WriteIndented=true }));
            Logger.LogInformation("FEEDBACK PROBE finished. Unloading test plugin and quitting test server.");
            Server.NextWorldUpdate(static ()=>Server.ExecuteCommand("quit"));
            Server.ExecuteCommand("css_plugins unload \"Superpower 1v5\"");
        },TimerFlags.STOP_ON_MAPCHANGE);
    }
    private static void Require(bool condition,string message) { if (!condition) throw new Exception(message); }
    private void Probe(string name,Action action)
    {
        try { action(); _probeResults.Add(new { name,passed=true }); Logger.LogInformation("NATIVE PASS {Name}",name); }
        catch (Exception e) { _probeResults.Add(new { name,passed=false,error=e.ToString() }); Logger.LogError(e,"NATIVE FAIL {Name}",name); }
    }
    private void NativeFeedbackProbe()
    {
        Probe("Six bots: one boss, five opponents, exactly two powers and 100 HP",()=> {
            Require(Mode.Live && Mode.Eligible().Count==6 && Mode.Eligible().Count(Mode.IsBoss)==1,"roster");
            Require(Mode.Opponents.Count()==5 && Powers.Loadout?.Powers.Count==2,"powers/teams");
            Require(Mode.Eligible().All(p=>Players.AlivePawn(p)?.Health==100),"health");
            _probeFirstBoss=Players.Key(Mode.Boss!);
        });
        Probe("All 18 powers construct and apply in the engine",()=> {
            foreach (var definition in PowerCatalog.All)
            {
                Powers.NativeProbePair(definition.Id,definition.Id=="infinite_ammo" ? "teleport" : "infinite_ammo");
                Powers.Tick(); Powers.SlowTick();
            }
        });
        Probe("Vampirism heals 100% of primary and secondary health damage",()=> {
            Powers.NativeProbePair("vampirism","infinite_ammo");
            var boss=Mode.Boss!; var victim=Mode.Opponents.First();
            var pawn=Players.AlivePawn(boss)!; var target=Players.AlivePawn(victim)!;
            pawn.Health=50; target.Health=100;
            Damage.NativeProbeHit(victim.EntityHandle.Raw,boss.EntityHandle.Raw,20);
            Require(pawn.Health==70 && target.Health==80,$"primary HP {pawn.Health}/{target.Health}");
            Damage.Deal(victim.EntityHandle.Raw,boss.EntityHandle.Raw,20,DamageTypes_t.DMG_BLAST);
            Require(pawn.Health==90,$"blast did not heal: {pawn.Health}"); Players.Heal(boss,100); Players.Heal(victim,100);
        });
        Probe("A null native result plus player_hurt fallback keeps effects working",()=> {
            ClearDamageTracking(); Powers.NativeProbePair("vampirism","infinite_ammo");
            var boss=Mode.Boss!; var victim=Mode.Opponents.First(); var pawn=Players.AlivePawn(boss)!;
            pawn.Health=50; Players.AlivePawn(victim)!.Health=80;
            AfterDamage(Players.AlivePawn(victim)!,new CTakeDamageInfo(IntPtr.Zero),new CTakeDamageResult(IntPtr.Zero));
            var hurt=new EventPlayerHurt(true) { Userid=victim,Attacker=boss,DmgHealth=20,Health=80,Weapon="glock" };
            hurt.FireEvent(false);
            Require(Config.Enabled && pawn.Health==70,$"enabled={Config.Enabled}, HP={pawn.Health}");
            Players.Heal(boss,100); Players.Heal(victim,100); ClearDamageTracking();
        });
        Probe("Slot assignment swaps existing powers without resetting cooldown",()=> {
            Powers.NativeProbePair("teleport","infinite_ammo");
            Powers.Loadout!.StartCooldown("teleport",Server.CurrentTime,15);
            Powers.Give("teleport",1);
            Require(Powers.Loadout.Powers[1].Id=="teleport" && Powers.Active[1].Id=="teleport","slot swap");
            Require(Powers.Loadout.Remaining("teleport",Server.CurrentTime)>14,"cooldown reset");
        });
        Probe("Speed is 2.5x and both gravity fields restore on cleanup",()=> {
            Powers.NativeProbePair("teleport","infinite_ammo"); var pawn=Players.AlivePawn(Mode.Boss)!;
            var speed=pawn.VelocityModifier; var gravity=pawn.GravityScale; var actual=pawn.ActualGravityScale;
            Powers.NativeProbePair("super_speed","low_gravity"); Powers.Tick();
            Require(Math.Abs(pawn.VelocityModifier-speed*2.5f)<.01,"speed multiplier");
            Require(Math.Abs(pawn.GravityScale-gravity*.35f)<.01 && Math.Abs(pawn.ActualGravityScale-actual*.35f)<.01,"gravity fields");
            Powers.NativeProbePair("teleport","infinite_ammo");
            Require(Math.Abs(pawn.VelocityModifier-speed)<.01 && Math.Abs(pawn.GravityScale-gravity)<.01 && Math.Abs(pawn.ActualGravityScale-actual)<.01,"movement cleanup");
        });
        Probe("Unlimited teleport reaches beyond the old 450-unit distance",()=> {
            Powers.NativeProbePair("teleport","infinite_ammo"); var pawn=Players.AlivePawn(Mode.Boss)!;
            var original=Players.Copy(pawn.AbsOrigin!); var angles=pawn.EyeAngles; var oldAngles=new QAngle(angles.X,angles.Y,angles.Z);
            var farthest=0f;
            for (var yaw=0;yaw<360;yaw+=15)
            {
                pawn.Teleport(original,new QAngle(0,yaw,0),new Vector(0,0,0));
                pawn.EyeAngles.X=0; pawn.EyeAngles.Y=yaw;
                if (SafeTeleport.TryBlink(pawn,0)) farthest=Math.Max(farthest,Players.Distance(original,pawn.AbsOrigin!));
                if (farthest>450) break;
            }
            pawn.Teleport(original,oldAngles,new Vector(0,0,0));
            pawn.EyeAngles.X=oldAngles.X; pawn.EyeAngles.Y=oldAngles.Y;
            Require(farthest>450,$"maximum reachable test blink {farthest:0} units");
        });
        Probe("Invisibility applies automatically, reports active, and restores color",()=> {
            Powers.NativeProbePair("teleport","infinite_ammo"); var pawn=Players.AlivePawn(Mode.Boss)!; var color=pawn.Render;
            Powers.NativeProbePair("invisibility","infinite_ammo");
            Require(Effects.HiddenEntityCount>0 && Powers.Status("invisibility")=="active until round ends","automatic application");
            Powers.NativeProbePair("teleport","infinite_ammo"); Require(pawn.Render==color && Effects.HiddenEntityCount==0,"cleanup");
        });
        Probe("Infinite ammo refills an equipped firearm",()=> {
            Powers.NativeProbePair("teleport","infinite_ammo"); var service=Players.AlivePawn(Mode.Boss)!.WeaponServices!;
            var previous=service.ActiveWeapon.Raw;
            var gun=service.MyWeapons.Select(h=>h.Value).FirstOrDefault(Players.Firearm) ?? Mode.Boss!.GiveNamedItem<CBasePlayerWeapon>("weapon_glock");
            Require(Players.Firearm(gun),"no firearm");
            try { service.ActiveWeapon.Raw=gun!.EntityHandle.Raw; gun.Clip1=1; Powers.Tick(); Require(gun.Clip1==gun.VData!.MaxClip1,"clip"); }
            finally { service.ActiveWeapon.Raw=previous; }
        });
        Probe("Knockback schedules an impulse after native bullet damage",()=> {
            Powers.NativeProbePair("knockback","infinite_ammo"); var victim=Mode.Opponents.First(); var pawn=Players.AlivePawn(victim)!;
            var raw=pawn.EntityHandle.Raw; var start=Players.Copy(pawn.AbsOrigin!);
            Damage.NativeProbeHit(victim.EntityHandle.Raw,Mode.Boss!.EntityHandle.Raw,5);
            AddTimer(.15f,()=>Probe("Knockback produces real displacement after several physics frames",()=> {
                var target=Players.Entity<CCSPlayerPawn>(raw)!;
                Require(Players.Distance(start,target.AbsOrigin!)>8,$"displacement {Players.Distance(start,target.AbsOrigin!)}");
                Players.Heal(victim,100); Powers.NativeProbePair("wall_vision","infinite_ammo");
                AddTimer(.1f,()=>Probe("Wall Vision creates and cleans five opponent outline pairs",()=> {
                    Require(Effects.OutlineCount==5,$"outline count {Effects.OutlineCount}");
                    Powers.NativeProbePair("teleport","infinite_ammo"); Require(Effects.OutlineCount==0,"outline leak");
                }),TimerFlags.STOP_ON_MAPCHANGE);
            }),TimerFlags.STOP_ON_MAPCHANGE);
        });
    }
    private void GravityPhysicsProbe()
    {
        Probe("Gravity physics sample starts in clear air",()=> {
            Powers.NativeProbePair("teleport","infinite_ammo");
            var pawn=Players.AlivePawn(Mode.Boss)!; var raw=pawn.EntityHandle.Raw;
            var original=Players.Copy(pawn.AbsOrigin!); var air=new Vector(original.X,original.Y,original.Z+500);
            pawn.GroundEntity.Raw=uint.MaxValue; pawn.Flags &= ~1u; pawn.Teleport(air,null,new Vector(0,0,0));
            AddTimer(.25f,()=> {
                var normal=Players.Entity<CCSPlayerPawn>(raw)!; var normalVelocity=normal.AbsVelocity.Z;
                Powers.NativeProbePair("low_gravity","infinite_ammo");
                normal.GroundEntity.Raw=uint.MaxValue; normal.Flags &= ~1u; normal.Teleport(air,null,new Vector(0,0,0));
                AddTimer(.25f,()=>Probe("Low Gravity reduces actual falling acceleration",()=> {
                    var slower=Players.Entity<CCSPlayerPawn>(raw)!; var lowVelocity=slower.AbsVelocity.Z;
                    try { Require(normalVelocity < -40 && lowVelocity< -5 && Math.Abs(lowVelocity)<Math.Abs(normalVelocity)*.7f,$"normal vz={normalVelocity:0.0}, low vz={lowVelocity:0.0}"); }
                    finally { Powers.NativeProbePair("teleport","infinite_ammo"); slower.Teleport(original,null,new Vector(0,0,0)); }
                }),TimerFlags.STOP_ON_MAPCHANGE);
            },TimerFlags.STOP_ON_MAPCHANGE);
        });
    }
}
#endif
