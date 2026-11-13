using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Superpower1v5.Support;

namespace Superpower1v5.Powers.Combat;

public sealed class RapidFire() : Power("rapid_fire")
{
    private readonly Dictionary<uint,(int tick,float ratio)> _original = new();
    public override void WeaponFire(PowerContext c)
    {
        var raw = c.Pawn?.WeaponServices?.ActiveWeapon.Value?.EntityHandle.Raw;
        if (raw is null) return;
        c.Plugin.Defer(() => {
            if (!c.Plugin.Powers.Active.Contains(this)) return;
            var w = Players.Entity<CBasePlayerWeapon>(raw.Value);
            if (!Players.Firearm(w) || c.Pawn is not { } p || w!.OwnerEntity.Raw != p.EntityHandle.Raw) return;
            _original[raw.Value] = (w.NextPrimaryAttackTick,w.NextPrimaryAttackTickRatio);
            var remaining = w.NextPrimaryAttackTick + w.NextPrimaryAttackTickRatio - Server.TickCount;
            if (remaining <= 1) return;
            var next = Server.TickCount + Math.Max(1,remaining / Settings(c).Strength);
            w.NextPrimaryAttackTick = (int)next; w.NextPrimaryAttackTickRatio = next-(int)next;
            Utilities.SetStateChanged(w,"CBasePlayerWeapon","m_nNextPrimaryAttackTick");
            Utilities.SetStateChanged(w,"CBasePlayerWeapon","m_flNextPrimaryAttackTickRatio");
        });
    }
    private void Restore(uint raw)
    {
        var old = _original[raw]; var w = Players.Entity<CBasePlayerWeapon>(raw);
        if (w is { IsValid: true } && old.tick >= Server.TickCount)
        {
            w.NextPrimaryAttackTick = old.tick; w.NextPrimaryAttackTickRatio = old.ratio;
            Utilities.SetStateChanged(w,"CBasePlayerWeapon","m_nNextPrimaryAttackTick");
            Utilities.SetStateChanged(w,"CBasePlayerWeapon","m_flNextPrimaryAttackTickRatio");
        }
        _original.Remove(raw);
    }
    public override void Tick(PowerContext c)
    {
        foreach (var raw in _original.Keys.ToArray())
        {
            var w = Players.Entity<CBasePlayerWeapon>(raw);
            if (w is not { IsValid: true } || c.Pawn is not { } p || w.OwnerEntity.Raw != p.EntityHandle.Raw) Restore(raw);
        }
    }
    public override void Cleanup(PowerContext c) { foreach (var raw in _original.Keys.ToArray()) Restore(raw); }
}
public sealed class ExplosiveBullets() : Power("explosive_bullets")
{
    public override void BulletImpact(PowerContext c, Vector impact) => Impact(c,impact);
    private double _next;
    public void Impact(PowerContext c, Vector impact)
    {
        if (c.Now < _next || c.Pawn is not { } boss || c.Boss is not { } controller) return;
        _next = c.Now + Settings(c).Interval;
        var source = controller.EntityHandle.Raw;
        var radius = Settings(c).Radius; var strength = Settings(c).Strength;
        // Pull the origin slightly off the surface so the LOS test starts outside the wall.
        var eye = boss.AbsOrigin;
        if (eye is null) return;
        var dx = eye.X-impact.X; var dy = eye.Y-impact.Y; var dz = eye.Z+64-impact.Z;
        var len = Math.Max(1,MathF.Sqrt(dx*dx+dy*dy+dz*dz));
        var origin = new Vector(impact.X+dx/len*3,impact.Y+dy/len*3,impact.Z+dz/len*3);
        foreach (var opponent in c.Opponents.ToArray())
        {
            if (Players.AlivePawn(opponent) is not { } p || p.AbsOrigin is not { } pos) continue;
            var center = new Vector(pos.X,pos.Y,pos.Z+36);
            var distance = Players.Distance(origin,center);
            if (distance > radius || !SafeTeleport.Visible(origin,center,boss)) continue;
            var target = opponent.EntityHandle.Raw;
            var damage = strength * (1 - .5f*distance/radius);
            c.Plugin.Defer(() => c.Plugin.Damage.Deal(target,source,damage,DamageTypes_t.DMG_BLAST));
        }
        c.Plugin.Feedback.Blast(origin);
    }
}
