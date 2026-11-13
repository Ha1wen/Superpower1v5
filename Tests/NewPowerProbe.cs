#if SERVER_SMOKE
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Superpower1v5.Models;
using Superpower1v5.Powers.Active;
using Superpower1v5.Powers.Combat;
using Superpower1v5.Support;
using GameTrace=CounterStrikeSharp.API.Modules.Utils.Trace;

namespace Superpower1v5;

public sealed partial class Plugin
{
    private void ScheduleNewPowerTests()
    {
        AddTimer(12,()=>Probe("All 16 powers apply; six bots retain one boss and two powers",()=> {
            Require(Mode.Live && Mode.Eligible().Count==6 && Mode.Opponents.Count()==5,"roster");
            foreach (var power in PowerCatalog.All)
            {
                Powers.NativeProbePair(power.Id,power.Id=="infinite_ammo" ? "teleport" : "infinite_ammo"); Powers.Tick();
            }
            Powers.NativeProbePair("bhop","infinite_ammo");
            Require(Powers.Loadout!.Powers.Count==2,"power count");
            var pawn=Players.AlivePawn(Mode.Boss)!; var origin=Players.Copy(pawn.AbsOrigin!);
            var buttons=pawn.MovementServices!.Buttons.ButtonStates[0];
            pawn.MovementServices.Buttons.ButtonStates[0]|=(ulong)PlayerButtons.Jump;
            pawn.Flags|=(uint)PlayerFlags.FL_ONGROUND;
            Powers.Tick();
            Require(pawn.AbsVelocity.Z>250,"hold-jump impulse absent");
            AddTimer(.15f,()=>Probe("Bhop launches the boss through real physics",()=> {
                Require(pawn.AbsOrigin!.Z>origin.Z+8,"no upward movement");
                pawn.MovementServices!.Buttons.ButtonStates[0]=buttons; pawn.Teleport(origin,null,new Vector(0,0,0));
                Powers.NativeProbePair("teleport","infinite_ammo");
            }),TimerFlags.STOP_ON_MAPCHANGE);
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(12.4f,()=>Probe("Tiny shrinks model, view offset, and native collision bounds",()=> {
            var pawn=Players.AlivePawn(Mode.Boss)!; var node=pawn.CBodyComponent!.SceneNode!;
            var scale=node.Scale; var eye=pawn.ViewOffset.Z; var width=pawn.Collision.Maxs.X-pawn.Collision.Mins.X;
            GameTrace.GetEntityWorldSpaceAABB(pawn,out var beforeMin,out var beforeMax);
            Powers.NativeProbePair("tiny","infinite_ammo");
            AddTimer(.15f,()=>Probe("Tiny survives physics updates and restores normal size",()=> {
                try
                {
                    Require(Math.Abs(node.Scale-scale*.5f)<.01,"model scale");
                    Require(pawn.ViewOffset.Z<eye*.7f,"view height");
                    Require(pawn.Collision.Maxs.X-pawn.Collision.Mins.X<width*.7f,"collision width");
                    GameTrace.GetEntityWorldSpaceAABB(pawn,out var afterMin,out var afterMax);
                    Require(afterMax.X-afterMin.X<(beforeMax.X-beforeMin.X)*.75f,$"world bounds before {beforeMax.X-beforeMin.X}, after {afterMax.X-afterMin.X}");
                }
                finally { Powers.NativeProbePair("teleport","infinite_ammo"); }
                Require(Math.Abs(node.Scale-scale)<.01 && Math.Abs(pawn.ViewOffset.Z-eye)<1,"size cleanup");
            }),TimerFlags.STOP_ON_MAPCHANGE);
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(13,()=>Probe("Sandevistan freezes five opponents and blocks weapon attacks",()=> {
            Powers.NativeProbePair("sandevistan","pyromaniac"); Powers.Activate(0);
            var power=(Sandevistan)Powers.Active[0]; Require(power.FrozenCount==5,"frozen roster");
            foreach (var opponent in Mode.Opponents)
            {
                var pawn=Players.AlivePawn(opponent)!;
                Require(pawn.ActualMoveType==MoveType_t.MOVETYPE_NONE,"movement not frozen");
                Require(pawn.WeaponServices!.As<CCSPlayer_WeaponServices>().NextAttack>Server.CurrentTime+9,"attacks not blocked");
            }
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(14,()=>Probe("Pyromaniac creates a bounded, visible floor fire pool",()=> {
            var opponent=Mode.Opponents.First(); var pawn=Players.AlivePawn(opponent)!; var origin=Players.Copy(pawn.AbsOrigin!);
            Powers.Impact(origin.X,origin.Y,origin.Z);
            Require(((Pyromaniac)Powers.Active[1]).PoolCount>0,"no floor fire");
            AddTimer(1,()=>Probe("Fire deals real burn damage; smoke extinguishes it",()=> {
                var afterPool=pawn.Health;
                Require(afterPool<100 && afterPool>0,$"HP after fire {afterPool}");
                Powers.Smoke(origin.X,origin.Y,origin.Z);
                Require(((Pyromaniac)Powers.Active[1]).PoolCount==0,"smoke did not extinguish"); Players.Heal(opponent,100);
            }),TimerFlags.STOP_ON_MAPCHANGE);
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(23.5f,()=>Probe("Sandevistan expires after its full 10 seconds and restores opponents",()=> {
            Require(((Sandevistan)Powers.Active[0]).FrozenCount==0,"freeze did not expire");
            Require(Mode.Opponents.All(p=>Players.AlivePawn(p) is { } pawn && pawn.ActualMoveType==MoveType_t.MOVETYPE_WALK &&
                (pawn.Flags&(uint)PlayerFlags.FL_FROZEN)==0),"opponents remain frozen");
            Powers.NativeProbePair("sandevistan","infinite_ammo"); Powers.Activate(0);
            Powers.NativeProbePair("teleport","infinite_ammo");
            Require(Mode.Opponents.All(p=>Players.AlivePawn(p)!.ActualMoveType==MoveType_t.MOVETYPE_WALK),"removal did not restore opponents");
        }),TimerFlags.STOP_ON_MAPCHANGE);
        // Random Kill has a separate suite covering both teams and boss self-selection.
        AddTimer(26,()=> {
            Powers.NativeProbePair("teleport","infinite_ammo");
            Server.ExecuteCommand("mp_restartgame 1");
        },TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(32,()=> {
            Probe("Round cleanup leaves normal health, one boss, two powers, and no old entities",()=> {
                Require(Mode.Live && Mode.Eligible().Count==6 && Mode.Opponents.Count()==5 && Powers.Loadout!.Powers.Count==2,"roster");
                Require(Mode.Eligible().All(p=>Players.AlivePawn(p)?.Health==100),"health");
                Require(TrackedEntityCount==0,"temporary entity leak");
            });
            File.WriteAllText(Path.Combine(ProbeDirectory,"native-probe-results.json"),JsonSerializer.Serialize(_probeResults,new JsonSerializerOptions { WriteIndented=true }));
            Logger.LogInformation("NEW POWERS PROBE finished; unloading isolated test plugin.");
            Server.NextWorldUpdate(static ()=>Server.ExecuteCommand("quit")); Server.ExecuteCommand("css_plugins unload \"Superpower 1v5\"");
        },TimerFlags.STOP_ON_MAPCHANGE);
    }
}
#endif
