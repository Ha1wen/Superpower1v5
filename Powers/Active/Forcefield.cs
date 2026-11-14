using Superpower1v5.Support;

namespace Superpower1v5.Powers.Active;

public sealed class Forcefield() : Power("forcefield")
{
    private double _until;
    public override int DefensePriority => 100;
    public override bool IsActive => CounterStrikeSharp.API.Server.CurrentTime<_until;
    public override string? Status => IsActive ? $"protected {Math.Max(0,_until-CounterStrikeSharp.API.Server.CurrentTime):0.0}s" : null;
    public override bool Activate(PowerContext c)
    {
        if (c.Pawn is null) return false;
        _until=c.Now+Settings(c).Duration;
        c.Tell("Forcefield: invincible for 10 seconds.");
        return true;
    }
    public override bool PreventLethal(PowerContext c,DamageEvent hit)
    {
        if (!hit.BossVictim || c.Now>=_until || hit.Info is not { } info) return false;
        info.Damage=0; info.OriginalDamage=0; info.TotalledDamage=0;
        return true;
    }
    public override void Cleanup(PowerContext c) => _until=0;
}
