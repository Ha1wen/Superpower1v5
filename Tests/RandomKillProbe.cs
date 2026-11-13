#if SERVER_SMOKE
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using Superpower1v5.Support;

namespace Superpower1v5;

public sealed partial class Plugin
{
    private sealed class PlayerSequenceRandom(Plugin plugin,params uint[] targets) : Random
    {
        public int Calls { get; private set; }
        public int PoolSize { get; private set; }
        public override int Next(int maxValue)
        {
            PoolSize=maxValue;
            var candidates=plugin.Mode.Eligible().Where(p=>Players.AlivePawn(p) is not null).ToArray();
            Require(candidates.Length==maxValue,"target pool excludes a living player or includes a dead player");
            var desired=targets[Math.Min(Calls++,targets.Length-1)];
            var index=Array.FindIndex(candidates,p=>p.EntityHandle.Raw==desired);
            Require(index>=0,"requested test player is not alive"); return index;
        }
    }
    private PlayerSequenceRandom? _killRandom;
    private uint _firstKill;
    private int LivingPlayers() => Mode.Eligible().Count(p=>Players.AlivePawn(p) is not null);
    private void ScheduleRandomKillTests()
    {
        AddTimer(12,()=>Probe("Direct kill selects one of all six living players without blast effects or Vampirism healing",()=> {
            Require(Mode.Live && Mode.Opponents.Count()==5 && LivingPlayers()==6,"roster");
            var victims=Mode.Opponents.ToArray(); _firstKill=victims[0].EntityHandle.Raw;
            _killRandom=new(this,victims[0].EntityHandle.Raw,victims[1].EntityHandle.Raw);
            Powers.NativeProbePair("random_explosion","vampirism",_killRandom);
            Players.AlivePawn(Mode.Boss)!.Health=50; Players.AlivePawn(victims[0])!.ArmorValue=100;
            Powers.Activate(0);
            Require(Players.AlivePawn(victims[0]) is null && LivingPlayers()==5,"target survived or another player died");
            Require(Players.AlivePawn(Mode.Boss)!.Health==50,"direct death generated Vampirism healing");
            Require(_killRandom.PoolSize==6 && TrackedEntityCount==0,"boss omitted or blast effect created");
            Require(Powers.Loadout!.Remaining("random_explosion",Server.CurrentTime)>19.9,"20-second cooldown missing");
            Powers.Activate(0); Powers.Give("random_explosion",1); Powers.Activate(1);
            Require(_killRandom.Calls==1 && LivingPlayers()==5,"repeat/slot swap bypassed cooldown");
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(31,()=>Probe("Direct kill stays blocked at 19 seconds",()=> {
            Require(Powers.Loadout!.Remaining("random_explosion",Server.CurrentTime)>.5,"cooldown expired early");
            Powers.Activate(1); Require(_killRandom!.Calls==1 && LivingPlayers()==5,"cooldown bypass");
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(33,()=>Probe("After 20 seconds the next kill excludes dead players and still includes the boss",()=> {
            Require(Powers.Loadout!.Remaining("random_explosion",Server.CurrentTime)==0,"cooldown did not expire");
            Powers.Activate(1);
            Require(LivingPlayers()==4 && Players.AlivePawn(Players.Resolve(_firstKill)) is null,"invalid second kill");
            Require(_killRandom!.Calls==2 && _killRandom.PoolSize==5 && TrackedEntityCount==0,"invalid alive pool or explosion effect");
            foreach (var entry in Config.Powers) entry.Value.Enabled=entry.Key is "random_explosion" or "infinite_ammo";
            Server.ExecuteCommand("mp_restartgame 1");
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(38,()=>Probe("Selecting the boss kills them even with Second Chance and safely clears their powers",()=> {
            Require(Mode.Live && LivingPlayers()==6,"new round");
            var boss=Mode.Boss!; var random=new PlayerSequenceRandom(this,boss.EntityHandle.Raw);
            Powers.NativeProbePair("random_explosion","second_chance",random);
            var previousLoadout=Powers.Loadout!;
            Powers.Activate(0);
            Require(Players.AlivePawn(boss) is null && Mode.Opponents.Count(p=>Players.AlivePawn(p) is not null)==5,"boss was protected or opponent died");
            Require(Config.Enabled && Powers.Active.Count==0 && TrackedEntityCount==0,"boss death did not clean up safely");
            Require(previousLoadout.Remaining("random_explosion",Server.CurrentTime)>19.9,"boss death broke cooldown recording");
            Server.ExecuteCommand("mp_restartgame 1");
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(44,()=> {
            Probe("Following a boss self-kill the next round has normal health and one boss with two powers",()=> {
                Require(Config.Enabled && Mode.Live && LivingPlayers()==6 && Mode.Opponents.Count()==5 && Powers.Loadout?.Powers.Count==2,"round state");
                Require(Mode.Eligible().All(p=>Players.AlivePawn(p)?.Health==100) && TrackedEntityCount==0,"health or leaked effects");
            });
            File.WriteAllText(Path.Combine(ProbeDirectory,"native-probe-results.json"),JsonSerializer.Serialize(_probeResults,new JsonSerializerOptions { WriteIndented=true }));
            Logger.LogInformation("RANDOM KILL PROBE finished; unloading isolated test plugin.");
            Server.NextWorldUpdate(static ()=>Server.ExecuteCommand("quit")); Server.ExecuteCommand("css_plugins unload \"Superpower 1v5\"");
        },TimerFlags.STOP_ON_MAPCHANGE);
    }
}
#endif
