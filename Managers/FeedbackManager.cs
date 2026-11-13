using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Superpower1v5.Models;
using Superpower1v5.Support;

namespace Superpower1v5.Managers;

public sealed class FeedbackManager(Plugin plugin)
{
    private double _nextHud;
    private readonly Dictionary<uint,double> _centerReservedUntil=new();
    private int _revealedGeneration=-1;
    public void ReserveCenter(double until,CCSPlayerController? player=null)
    { if ((player ?? plugin.Mode.Boss) is { } owner) _centerReservedUntil[owner.EntityHandle.Raw]=until; }
    public void Round()
    {
        if (plugin.Mode.Boss is not { } boss || plugin.Powers.Loadout is null) return;
        _centerReservedUntil.Clear(); _nextHud=0;
        plugin.Broadcast($"SUPERPOWER ROUND | PLAYER: {boss.PlayerName}");
        foreach (var player in plugin.Powers.PoweredPlayers) Loadout(player);
    }
    public void Loadout(CCSPlayerController player)
    {
        if (plugin.Powers.GetLoadout(player) is not { } loadout) return;
        for (var i=0;i<2;i++)
        {
            var p=loadout.Powers[i]; player.PrintToChat(Plugin.Prefix+$"POWER {i+1}: {p.Name.ToUpperInvariant()}");
            player.PrintToChat(Plugin.Prefix+p.Description);
        }
        if (plugin.Config.EnableHUD)
            player.PrintToCenter($"YOUR POWERS\n{loadout.Powers[0].Name} + {loadout.Powers[1].Name}");
        Ability(player);
        if (plugin.Config.PublicPowerAnnouncements) PublicLoadout(player);
    }
    public void PublicLoadout(CCSPlayerController player)
    {
        if (plugin.Powers.GetLoadout(player) is { } loadout)
            plugin.Broadcast($"{player.PlayerName}'s powers: {string.Join(" + ",loadout.Powers.Select(p=>p.Name))}");
    }
    public void RevealRound()
    {
        if (_revealedGeneration==plugin.Mode.Generation) return;
        _revealedGeneration=plugin.Mode.Generation;
        foreach (var reveal in plugin.Powers.Reveals)
            plugin.Broadcast($"ROUND REVEAL | {reveal.PlayerName}: {string.Join(" + ",reveal.Powers.Select(p=>p.Name))}");
    }
    public void Ability(CCSPlayerController? player=null)
    {
        player ??=plugin.Mode.Boss;
        if (plugin.Config.EnableSounds && Players.AlivePawn(player) is { } pawn)
            pawn.EmitSound("Buttons.snd9",new RecipientFilter(player!));
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
        if (!plugin.Config.EnableHUD || Server.CurrentTime<_nextHud) return;
        _nextHud=Server.CurrentTime+2;
        foreach (var player in plugin.Powers.PoweredPlayers)
        {
            if (!plugin.Mode.CanUsePlayerPowers(player) || Server.CurrentTime<_centerReservedUntil.GetValueOrDefault(player.EntityHandle.Raw) || plugin.Powers.GetLoadout(player) is not { } loadout) continue;
            var lines=loadout.Powers.Select((p,i)=>$"{i+1}. {p.Name}: {plugin.Powers.Status(p.Id,player)}");
            player.PrintToCenter(string.Join("\n",lines));
        }
    }
}
