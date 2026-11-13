using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Cvars;
using Superpower1v5.Models;
using Superpower1v5.Support;
using GameTrace=CounterStrikeSharp.API.Modules.Utils.Trace;

namespace Superpower1v5.Powers.Combat;

public sealed class SecondChance() : Power("second_chance")
{
    private readonly RewindHistory _history=new();
    private uint? _pawn;
    private double _sampleAt,_protectedUntil;
    public int SnapshotCount => _history.Count;
#if SERVER_SMOKE
    public RewindFrame? LastRestoredFrame { get; private set; }
#endif
    public override void Apply(PowerContext c) => Record(c);
    public override void Tick(PowerContext c) { if (c.Now>=_sampleAt) Record(c); }
    private void Record(PowerContext c)
    {
        if (c.Pawn is not { } pawn || pawn.AbsOrigin is not { } position) return;
        if (_pawn!=pawn.EntityHandle.Raw) { _history.Clear(); _pawn=pawn.EntityHandle.Raw; }
        _sampleAt=c.Now+.1;
        _history.Record(new(c.Now,new(position.X,position.Y,position.Z),new(pawn.EyeAngles.X,pawn.EyeAngles.Y,pawn.EyeAngles.Z),
            new(pawn.AbsVelocity.X,pawn.AbsVelocity.Y,pawn.AbsVelocity.Z),pawn.Health,pawn.ArmorValue));
    }
    public override bool PreventLethal(PowerContext c,DamageEvent damage)
    {
        if (!damage.BossVictim || c.Pawn is not { } pawn || damage.Info is not { } info) return false;
        if (c.Now<_protectedUntil) { Zero(info); return true; }
        if (Math.Ceiling(ExpectedHealthDamage(damage,pawn))<pawn.Health || !c.Loadout.Consumed.Add(Id)) return false;
        Zero(info); _protectedUntil=c.Now+.4;
        RewindFrame? frame=null; Vector? landing=null;
        foreach (var candidate in _history.Candidates(c.Now,Settings(c).Duration))
        { if (Landing(pawn,candidate) is { } clear) { frame=candidate; landing=clear; break; } }
#if SERVER_SMOKE
        LastRestoredFrame=frame;
#endif
        pawn.Health=Math.Clamp(frame?.Health ?? (int)Settings(c).Strength,1,pawn.MaxHealth);
        if (frame is not null)
        {
            pawn.ArmorValue=Math.Clamp(frame.Armor,0,100);
            pawn.Teleport(landing,new QAngle(frame.Angles.X,frame.Angles.Y,frame.Angles.Z),new Vector(frame.Velocity.X,frame.Velocity.Y,frame.Velocity.Z));
            var raw=pawn.EntityHandle.Raw; var destination=Players.Copy(landing!); var restored=frame;
            // A weapon hit may still be inside lag compensation. Reapply after its rollback.
            c.Plugin.Defer(()=> {
                if (c.Contains(this) && c.Pawn is { } alive && alive.EntityHandle.Raw==raw)
                    alive.Teleport(destination,new QAngle(restored.Angles.X,restored.Angles.Y,restored.Angles.Z),new Vector(restored.Velocity.X,restored.Velocity.Y,restored.Velocity.Z));
            });
            Utilities.SetStateChanged(pawn,"CCSPlayerPawn","m_ArmorValue");
        }
        Utilities.SetStateChanged(pawn,"CBaseEntity","m_iHealth");
        c.Tell("Second Chance rewound you to safety."); c.Boss!.PrintToCenter("SECOND CHANCE\nPosition and health restored");
        return true;
    }
    private static void Zero(CTakeDamageInfo info) { info.Damage=0; info.OriginalDamage=0; info.TotalledDamage=0; }
    private static float ExpectedHealthDamage(DamageEvent damage,CCSPlayerPawn pawn)
    {
        var info=damage.Info!; var amount=Math.Max(info.Damage,info.TotalledDamage);
        // API 380's callback precedes CS2's hitbox/armor scaling. HitGroupId can
        // still be INVALID here; GetHitGroup reads the actual bullet trace.
        if (!damage.Bullet || damage.Synthetic || info.HitGroupId!=HitGroup_t.HITGROUP_INVALID) return amount;
        var group=info.GetHitGroup();
        if (group is HitGroup_t.HITGROUP_INVALID or HitGroup_t.HITGROUP_GENERIC) return amount;
        var weapon=Players.AlivePawn(damage.Attacker)?.WeaponServices?.ActiveWeapon.Value?.As<CCSWeaponBase>().VData;
        var part=group==HitGroup_t.HITGROUP_HEAD ? "head" : "body";
        var team=damage.Victim.TeamNum==2 ? "t" : "ct";
        var scale=ConVar<float>.Find($"mp_damage_scale_{team}_{part}")?.Value ?? 1f;
        var helmet=pawn.ItemServices?.As<CCSPlayer_ItemServices>().HasHelmet==true;
        return BulletHealthDamage.Estimate(amount,group,weapon?.HeadshotMultiplier ?? 4,scale,pawn.ArmorValue,helmet,weapon?.ArmorRatio ?? 2);
    }
    private static Vector? Landing(CCSPlayerPawn pawn,RewindFrame frame)
    {
        // Native player capsules can stand slightly inside the bounding-box floor plane.
        // Allow a small upward clearance adjustment at the recorded location.
        foreach (var lift in new[] { 2,8,16,32 })
        {
            var point=new Vector(frame.Position.X,frame.Position.Y,frame.Position.Z+lift);
            var trace=GameTrace.TraceHullShape(point,point,pawn.Collision.Mins,pawn.Collision.Maxs,pawn,new() { InteractsWith=Masks.PlayerSolidBrushOnly });
            if (!trace.IsAllSolid && !trace.DidHit()) return point;
        }
        return null;
    }
    public override void Cleanup(PowerContext c) { _history.Clear(); _pawn=null; _protectedUntil=0; }
}
