using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Superpower1v5.Models;
using Superpower1v5.Support;

namespace Superpower1v5.Managers;

public sealed class FeedbackManager(Plugin plugin)
{
    private double _nextHud;
    private double _centerReservedUntil;
    public void ReserveCenter(double until) => _centerReservedUntil=until;
    public void Round()
    {
        if (plugin.Mode.Boss is not { } boss || plugin.Powers.Loadout is not { } loadout) return;
        plugin.Broadcast($"SUPERPOWER ROUND | PLAYER: {boss.PlayerName}");
        for (var i=0;i<2;i++)
        {
            var p=loadout.Powers[i]; plugin.Broadcast($"POWER {i+1}: {p.Name.ToUpperInvariant()}");
            boss.PrintToChat(Plugin.Prefix+p.Description);
        }
        if (plugin.Config.EnableHUD)
            foreach (var p in Utilities.GetPlayers().Where(Players.Connected))
                p.PrintToCenter($"SUPERPOWER ROUND\n{boss.PlayerName}\n{loadout.Powers[0].Name} + {loadout.Powers[1].Name}");
        Ability();
    }
    public void Ability()
    {
        if (plugin.Config.EnableSounds && Players.AlivePawn(plugin.Mode.Boss) is { } pawn)
            pawn.EmitSound("Buttons.snd9");
    }
    public void Blast(Vector origin)
    {
        Particle("particles/explosions_fx/explosion_basic.vpcf",origin,.6f);
    }
    public uint Particle(string effect,Vector origin,float lifetime=0)
    {
        // Stock particle; removed by an owned map timer. Damage does not depend on visuals.
        var particle=Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
        if (particle is not { IsValid: true }) return 0;
        particle.EffectName=effect;
        particle.StartActive=true; particle.Teleport(origin,null,null); particle.DispatchSpawn();
        var raw=particle.EntityHandle.Raw;
        plugin.TrackEntity(raw);
        if (lifetime>0) plugin.AddTimer(lifetime,()=>plugin.RemoveTrackedEntity(raw),CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        return raw;
    }
    public void Hud()
    {
        if (!plugin.Config.EnableHUD || !plugin.Mode.CanUsePowers || Server.CurrentTime<_nextHud || Server.CurrentTime<_centerReservedUntil || plugin.Powers.Loadout is not { } loadout) return;
        _nextHud=Server.CurrentTime+2;
        var lines=loadout.Powers.Select((p,i)=>$"{i+1}. {p.Name}: {plugin.Powers.Status(p.Id)}");
        plugin.Mode.Boss!.PrintToCenter(string.Join("\n",lines));
    }
}
