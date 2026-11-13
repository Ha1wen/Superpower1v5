#if SERVER_SMOKE
using System.Drawing;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using Superpower1v5.Models;
using Superpower1v5.Support;

namespace Superpower1v5;

public sealed partial class Plugin
{
    private Color _persistentOriginalColor;
    private void SchedulePersistentPowerTests()
    {
        AddTimer(12,()=>Probe("All 18 powers apply; migrated config excludes Yoru and rarity",()=> {
            Require(Mode.Live && Mode.Eligible().Count==6 && Mode.Opponents.Count()==5,"roster");
            Require(Config.Version==6 && Config.Powers.Count==18 && !Config.Powers.ContainsKey("yoru_flash"),"migration");
            foreach (var definition in PowerCatalog.All)
            {
                Powers.NativeProbePair(definition.Id,definition.Id=="infinite_ammo" ? "teleport" : "infinite_ammo");
                Powers.Tick(); Powers.SlowTick();
            }
            Powers.NativeProbePair("teleport","infinite_ammo");
            _persistentOriginalColor=Players.AlivePawn(Mode.Boss)!.Render;
            Powers.NativeProbePair("invisibility","wall_vision");
            Require(Powers.Active.All(p=>p.IsActive),"automatic application");
            Require(Effects.HiddenEntityCount>0 && Effects.OutlineCount==5,"effects absent without activation");
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(20,()=>Probe("Both passive effects remain active without ability commands",()=> {
            Require(Effects.HiddenEntityCount>0,"invisibility expired");
            Require(Effects.OutlineCount==5,"wall vision expired");
            Require(Powers.Active.All(p=>p.IsActive),"active state lost");
            Require(Powers.Status("invisibility")=="active until round ends" && Powers.Status("wall_vision")=="active until round ends","status");
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(39,()=>Probe("Both effects survive 27 seconds and a slot swap without reset",()=> {
            Require(Effects.HiddenEntityCount>0 && Effects.OutlineCount==5,"long duration");
            Powers.Give("invisibility",1);
            Require(Powers.Loadout!.Powers[1].Id=="invisibility" && Powers.Active.All(p=>p.IsActive),"swap lost active state");
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(40,()=>Probe("Removing Wall Vision cleans outlines while retaining Invisibility",()=> {
            Powers.Give("infinite_ammo",0);
            Require(Effects.OutlineCount==0,"outlines leaked");
            Require(Effects.HiddenEntityCount>0,"other effect lost");
            Powers.Give("wall_vision",0);
            Require(Powers.Status("wall_vision")=="active until round ends" && Effects.OutlineCount==5,"reassigned wall vision did not apply automatically");
            foreach (var entry in Config.Powers) entry.Value.Enabled=entry.Key is "invisibility" or "wall_vision";
            Server.ExecuteCommand("mp_restartgame 1");
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(45,()=>Probe("New round automatically enables both powers only for the new boss",()=> {
            Require(Mode.Live && Powers.Loadout!.Powers.Count==2 && Mode.Opponents.Count()==5,"round invariants");
            Require(Powers.Loadout!.Has("invisibility") && Powers.Loadout.Has("wall_vision") && Powers.Active.All(p=>p.IsActive),"new passive loadout inactive");
            Require(Effects.OutlineCount==5 && Effects.HiddenEntityCount>0,"new boss effects absent");
            Require(Mode.Opponents.All(p=>Players.AlivePawn(p)?.Render.A==255),"previous boss color leaked");
            foreach (var entry in Config.Powers) entry.Value.Enabled=true;
            var invisibleSlot=Powers.Loadout.Powers[0].Id=="invisibility" ? 0 : 1;
            Powers.Give("infinite_ammo",invisibleSlot);
            Require(Players.AlivePawn(Mode.Boss)!.Render.A==255,"removed invisibility color");
            Require(Effects.OutlineCount==5,"removing invisibility lost wall vision");
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(46,()=> {
            Probe("Ending the round clears active effects and restores normal colors",()=> {
                var pawn=Players.AlivePawn(Mode.Boss)!;
                Powers.NativeProbePair("invisibility","wall_vision");
                Mode.EndRound(0);
                Require(!Mode.Live && Powers.Active.Count==0 && Effects.OutlineCount==0,"end cleanup");
                Require(pawn.Render==_persistentOriginalColor,"color cleanup");
            });
            File.WriteAllText(Path.Combine(ProbeDirectory,"native-probe-results.json"),JsonSerializer.Serialize(_probeResults,new JsonSerializerOptions { WriteIndented=true }));
            Logger.LogInformation("PERSISTENT PROBE finished; unloading isolated test plugin.");
            Server.NextWorldUpdate(static ()=>Server.ExecuteCommand("quit")); Server.ExecuteCommand("css_plugins unload \"Superpower 1v5\"");
        },TimerFlags.STOP_ON_MAPCHANGE);
    }
}
#endif
