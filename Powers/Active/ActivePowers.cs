using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Superpower1v5.Models;
using Superpower1v5.Support;

namespace Superpower1v5.Powers.Active;

public sealed class Invisibility() : Power("invisibility")
{
    private bool _active;
    private readonly SilenceFade _fade=new();
    private byte _alpha=255;
    private bool _grounded;
    public override bool IsActive => _active;
    public override string? Status => _alpha==0 ? "hidden" : _alpha==255 ? "visible (sound)" : "fading";
    public static bool QuietMovement(CCSPlayerController player,CCSPlayerPawn pawn) =>
        pawn.IsWalking || (pawn.Flags&2)!=0 || (player.Buttons&(PlayerButtons.Speed|PlayerButtons.Walk|PlayerButtons.Duck))!=0;
    public override void Apply(PowerContext c)
    { _active=true; _grounded=c.Pawn is { } pawn && (pawn.Flags&1)!=0; Noise(c); }
    public override void Noise(PowerContext c)
    {
        _fade.Noise(c.Now); _alpha=255;
        c.Effects.ClearConcealment(); c.Effects.RestoreColorsExcept(new HashSet<uint>());
    }
    public override void Tick(PowerContext c)
    {
        if (!_active || c.Pawn is not { } pawn || c.Boss is not { } player) return;
        var grounded=(pawn.Flags&1)!=0;
        var speed=MathF.Sqrt(pawn.AbsVelocity.X*pawn.AbsVelocity.X+pawn.AbsVelocity.Y*pawn.AbsVelocity.Y);
        var weapon=pawn.WeaponServices?.ActiveWeapon.Value?.As<CCSWeaponBase>();
        var planting=weapon is { DesignerName:"weapon_c4" } && weapon.As<CC4>().StartedArming;
        if (weapon?.InReload==true || pawn.IsDefusing || planting || grounded && !_grounded ||
            grounded && speed>140 && !QuietMovement(player,pawn)) Noise(c);
        _grounded=grounded;
    }
    public override void SlowTick(PowerContext c)
    {
        if (!_active || c.Pawn is not { } p || c.Boss is not { } player) { Cleanup(c); return; }
        _alpha=_fade.Alpha(c.Now,c.Plugin.Config.InvisibilityQuietDelay,c.Plugin.Config.InvisibilityFadeDuration);
        if (_alpha==255) return;
        var attached=c.Effects.AttachedEntities(p);
        var colors=new HashSet<uint>();
        foreach (var entity in attached)
        {
            // The owner's first-person arms and weapon stay usable.
            if (entity.DesignerName.Contains("viewmodel",StringComparison.Ordinal)) continue;
            // Cast only known model classes. Other attachments are still excluded at
            // full concealment, without reading model fields from non-model entities.
            var name=entity.DesignerName;
            if (name!="player" && !name.StartsWith("weapon_",StringComparison.Ordinal) &&
                !name.StartsWith("prop_",StringComparison.Ordinal) && !name.Contains("wearable",StringComparison.Ordinal)) continue;
            var model=entity.As<CBaseModelEntity>();
            if (!model.IsValid) continue;
            c.Effects.Fade(model,_alpha); colors.Add(entity.EntityHandle.Raw);
        }
        c.Effects.RestoreColorsExcept(colors);
        if (_alpha==0) c.Effects.Conceal(p,player,attached); else c.Effects.ClearConcealment();
    }
    public override void Cleanup(PowerContext c)
    { _active=false; _alpha=255; c.Effects.ClearConcealment(); c.Effects.RestoreColorsExcept(new HashSet<uint>()); }
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
        c.Plugin.Feedback.ReserveCenter(c.Now+2,boss);
        var bearings = new List<string>();
        var living = new HashSet<uint>();
        foreach (var enemy in c.Opponents)
        {
            if (Players.AlivePawn(enemy) is not { } p || p.AbsOrigin is not { } pos) continue;
            living.Add(p.EntityHandle.Raw);
            c.Effects.Reveal(p,boss);
            var angle = MathF.Atan2(pos.Y-origin.Y,pos.X-origin.X)*180/MathF.PI;
            var relative = ((angle-c.Pawn!.EyeAngles.Y+540)%360)-180;
            bearings.Add($"{enemy.PlayerName}: {Players.Distance(origin,pos)/52.49f:0}m {relative:+0;-0;0}deg");
        }
        c.Effects.PruneOutlines(living);
        if (c.Now >= _nextBearing)
        { _nextBearing=c.Now+1; boss.PrintToCenter("WALL VISION\n"+string.Join("\n",bearings)); }
    }
    public override void Cleanup(PowerContext c)
    { _showing=false; c.Effects.ClearRadar(); c.Plugin.Feedback.ReserveCenter(0,c.Boss); }
}
