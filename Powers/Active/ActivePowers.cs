using CounterStrikeSharp.API.Core;
using Superpower1v5.Support;

namespace Superpower1v5.Powers.Active;

public sealed class Invisibility() : Power("invisibility")
{
    private bool _active;
    public override bool IsActive => _active;
    public override void Apply(PowerContext c) { _active=true; SlowTick(c); }
    public override void SlowTick(PowerContext c)
    {
        var keep = new HashSet<uint>();
        if (_active && c.Pawn is { } p)
        {
            keep.Add(p.EntityHandle.Raw); c.Plugin.Effects.Fade(p,(byte)Settings(c).Strength);
            if (p.WeaponServices is { } service)
                foreach (var handle in service.MyWeapons)
                    if (handle.Value is { IsValid: true } w && w.OwnerEntity.Raw == p.EntityHandle.Raw)
                    { keep.Add(w.EntityHandle.Raw); c.Plugin.Effects.Fade(w,(byte)Settings(c).Strength); }
        }
        c.Plugin.Effects.RestoreColorsExcept(keep);
    }
    public override void Cleanup(PowerContext c) { _active=false; c.Plugin.Effects.RestoreColorsExcept(new HashSet<uint>()); }
}
public sealed class Teleport() : Power("teleport")
{
    public override bool Activate(PowerContext c)
    {
        if (c.Pawn is { } p && SafeTeleport.TryBlink(p,Settings(c).Distance)) return true;
        c.Tell("No safe destination in that direction. Cooldown was not spent."); return false;
    }
}
public sealed class WallVision() : Power("wall_vision")
{
    private double _nextBearing;
    private bool _showing;
    public override bool IsActive => _showing;
    public override void Apply(PowerContext c)
    { _showing=true; SlowTick(c); }
    public override void SlowTick(PowerContext c)
    {
        if (!_showing) return;
        if (c.Boss is not { } boss || c.Pawn?.AbsOrigin is not { } origin)
        { Cleanup(c); return; }
        c.Plugin.Feedback.ReserveCenter(c.Now+2);
        var bearings = new List<string>();
        var living = new HashSet<uint>();
        foreach (var enemy in c.Opponents)
        {
            if (Players.AlivePawn(enemy) is not { } p || p.AbsOrigin is not { } pos) continue;
            living.Add(p.EntityHandle.Raw);
            c.Plugin.Effects.Reveal(p,boss);
            var angle = MathF.Atan2(pos.Y-origin.Y,pos.X-origin.X)*180/MathF.PI;
            var relative = ((angle-c.Pawn!.EyeAngles.Y+540)%360)-180;
            bearings.Add($"{enemy.PlayerName}: {Players.Distance(origin,pos)/52.49f:0}m {relative:+0;-0;0}deg");
        }
        c.Plugin.Effects.PruneOutlines(living);
        if (c.Now >= _nextBearing)
        { _nextBearing=c.Now+1; boss.PrintToCenter("WALL VISION\n"+string.Join("\n",bearings)); }
    }
    public override void Cleanup(PowerContext c)
    { _showing=false; c.Plugin.Effects.ClearRadar(); c.Plugin.Feedback.ReserveCenter(0); }
}
