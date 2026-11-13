using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Superpower1v5.Support;

namespace Superpower1v5.Powers.Combat;

public sealed class Vampirism() : Power("vampirism")
{
    private float _fraction;
    public override void AfterDamage(PowerContext context, DamageDealt damage)
    {
        // Secondary blast damage can heal too; healing itself never emits a damage event.
        if (!damage.Hit.BossAttacker) return;
        _fraction += damage.HealthLost * Settings(context).Strength;
        var heal = (int)_fraction;
        _fraction -= heal;
        Players.Heal(context.Boss, heal);
    }
}

public sealed class Knockback() : Power("knockback")
{
    private readonly Dictionary<uint, (float x, float y, float z)> _pending = new();
    public override void AfterDamage(PowerContext context, DamageDealt damage)
    {
        if (!damage.Hit.BossAttacker || !damage.Hit.Bullet || damage.Hit.Synthetic ||
            context.Pawn?.AbsOrigin is not { } from || Players.AlivePawn(damage.Hit.Victim) is not { } pawn || pawn.AbsOrigin is not { } to) return;
        var raw = pawn.EntityHandle.Raw;
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Max(1, MathF.Sqrt(dx * dx + dy * dy));
        var strength = Settings(context).Strength;
        var push = (x: dx / length * strength, y: dy / length * strength, z: Math.Clamp(strength * .3f, 180, 400));
        if (_pending.TryGetValue(raw, out var previous))
        {
            _pending[raw] = (previous.x + push.x, previous.y + push.y, Math.Max(previous.z, push.z));
            return;
        }
        _pending[raw] = push;
        // The hurt flow and grounded movement can overwrite an impulse applied synchronously.
        context.Plugin.Defer(() =>
        {
            if (!_pending.Remove(raw, out var impulse) || !context.Plugin.Powers.Active.Contains(this)) return;
            var target = Players.Entity<CCSPlayerPawn>(raw);
            if (target is not { IsValid: true, Health: > 0 } || target.AbsOrigin is not { } origin) return;
            var x = target.AbsVelocity.X + impulse.x;
            var y = target.AbsVelocity.Y + impulse.y;
            var horizontal = MathF.Sqrt(x * x + y * y);
            if (horizontal > 2000) { x *= 2000 / horizontal; y *= 2000 / horizontal; }
            var velocity = new Vector(x, y, Math.Max(target.AbsVelocity.Z, impulse.z));
            target.GroundEntity.Raw = uint.MaxValue;
            target.Flags &= ~1u;
            target.Teleport(new Vector(origin.X, origin.Y, origin.Z + 2), null, velocity);
            target.Velocity.X = velocity.X; target.Velocity.Y = velocity.Y; target.Velocity.Z = velocity.Z;
            Utilities.SetStateChanged(target, "CBaseEntity", "m_vecVelocity");
        });
    }
    public override void Cleanup(PowerContext context) => _pending.Clear();
}

public sealed class SecondChance() : Power("second_chance")
{
    public override bool PreventLethal(PowerContext context, DamageEvent damage)
    {
        if (!damage.BossVictim || context.Pawn is not { } pawn || damage.Info is not { } info || info.Damage < pawn.Health ||
            !context.Loadout.Consumed.Add(Id)) return false;
        pawn.Health = (int)Settings(context).Strength;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        context.Plugin.Broadcast($"{context.Boss!.PlayerName} used SECOND CHANCE!");
        context.Boss.PrintToCenter("SECOND CHANCE\nLethal hit prevented — health restored!");
        return true;
    }
}
