using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Superpower1v5.Support;
using GameTrace=CounterStrikeSharp.API.Modules.Utils.Trace;

namespace Superpower1v5.Powers.Combat;

public sealed class UnlimitedWallbang() : Power("unlimited_wallbang")
{
    private sealed class Shot(CCSPlayerPawn pawn,Vector origin,CCSWeaponBaseVData weapon)
    {
        public readonly uint Pawn=pawn.EntityHandle.Raw;
        public readonly int Tick=Server.TickCount;
        public readonly Vector Origin=origin;
        public readonly float Damage=weapon.Damage;
        public readonly float Range=Math.Clamp(weapon.Range,1,SafeTeleport.MaximumTraceDistance);
        public readonly float RangeModifier=weapon.RangeModifier;
        public readonly int Pellets=Math.Clamp(weapon.NumBullets,1,32);
        public readonly List<(Vector Ray,Vector Impact,uint Target,uint Pawn,float Damage,HitGroup_t HitGroup)> Hits=[];
        public readonly Dictionary<uint,int> NativeHits=[];
    }
    private static readonly TraceOptions PlayersOnly=new() { InteractsWith=Contents.Player|Contents.Hitbox };
    private Shot? _shot;

    private Shot? CurrentShot(PowerContext context)
    {
        if (context.Pawn is not { AbsOrigin:{ } position } pawn) return null;
        if (_shot is { } shot && shot.Tick==Server.TickCount && shot.Pawn==pawn.EntityHandle.Raw) return shot;
        var active=pawn.WeaponServices?.ActiveWeapon.Value;
        if (!Players.Firearm(active) || active!.As<CCSWeaponBase>().VData is not { Damage:>0,Range:>0 } weapon) return null;
        return _shot=new(pawn,new(position.X+pawn.ViewOffset.X,position.Y+pawn.ViewOffset.Y,position.Z+pawn.ViewOffset.Z),weapon);
    }

    public override void ModifyDamage(PowerContext context,DamageEvent hit)
    {
        if (!hit.BossAttacker || !hit.Bullet || hit.Synthetic || hit.Info is not { NumObjectsPenetrated:>0 } info ||
            CurrentShot(context) is not { } shot || Players.AlivePawn(hit.Victim) is not { AbsOrigin:{ } target }) return;
        var raw=hit.Victim.EntityHandle.Raw;
        shot.NativeHits[raw]=shot.NativeHits.GetValueOrDefault(raw)+1;
        // This callback runs before the engine applies hitgroup and armor scaling.
        var damage=DamageAtRange(shot.Damage,shot.RangeModifier,Players.Distance(shot.Origin,target));
        info.Damage=Math.Max(info.Damage,damage);
        info.OriginalDamage=Math.Max(info.OriginalDamage,damage);
        info.TotalledDamage=Math.Max(info.TotalledDamage,damage);
    }

    public override void BulletImpact(PowerContext context,Vector impact)
    {
        if (CurrentShot(context) is not { } shot || shot.Hits.Count>=shot.Pellets ||
            context.Pawn is not { } owner || context.Boss is not { } shooter) return;
        var distance=Players.Distance(shot.Origin,impact);
        if (!float.IsFinite(distance) || distance<1 || distance>=shot.Range) return;
        var ray=new Vector((impact.X-shot.Origin.X)/distance,(impact.Y-shot.Origin.Y)/distance,(impact.Z-shot.Origin.Z)/distance);
        if (shot.Hits.Any(hit=>Dot(hit.Ray,ray)>.999999f)) return;
        var end=new Vector(shot.Origin.X+ray.X*shot.Range,shot.Origin.Y+ray.Y*shot.Range,shot.Origin.Z+ray.Z*shot.Range);
        var target=GameTrace.TraceEndShape(shot.Origin,end,owner,PlayersOnly);
        var targetDistance=shot.Range*target.Fraction;
        if (!target.DidHit() || targetDistance<=distance+.5f || target.HitEntity() is not { IsValid:true,DesignerName:"player" } entity ||
            Players.Controller(entity.As<CBaseEntity>()) is not { } player || !context.Plugin.Mode.Enemies(shooter,player)) return;
        shot.Hits.Add((ray,target.EndPos,player.EntityHandle.Raw,entity.EntityHandle.Raw,
            DamageAtRange(shot.Damage,shot.RangeModifier,targetDistance),HitGroup(target)));
        if (shot.Hits.Count==1) context.Plugin.Defer(()=>FinishShot(context,shooter.EntityHandle.Raw,shot));
    }

    private void FinishShot(PowerContext context,uint shooter,Shot shot)
    {
        if (!context.Contains(this) || context.Pawn?.EntityHandle.Raw!=shot.Pawn) return;
        foreach (var hit in shot.Hits)
        {
            var nativeHits=shot.NativeHits.GetValueOrDefault(hit.Target);
            if (nativeHits>0)
            {
                shot.NativeHits[hit.Target]=nativeHits-1;
                continue;
            }
            if (Players.AlivePawn(Players.Resolve(hit.Target))?.EntityHandle.Raw==hit.Pawn)
                context.Plugin.Damage.DealBullet(hit.Target,shooter,hit.Damage,hit.HitGroup,hit.Impact);
        }
    }
    internal static float DamageAtRange(float damage,float modifier,float distance) =>
        damage*MathF.Pow(Math.Clamp(modifier,.01f,1),Math.Max(0,distance)/500);
    private static float Dot(Vector a,Vector b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    internal static HitGroup_t HitGroup(TraceResult result)
    {
        // The API exposes CHitBox as an opaque pointer. GroupId follows three
        // CUtlStrings, two Vectors, the radius, and the bone hash in the pinned ABI.
        var hitbox=result.Hitbox();
        if (hitbox==IntPtr.Zero) return HitGroup_t.HITGROUP_CHEST;
        var group=(HitGroup_t)Marshal.ReadInt32(hitbox,0x38);
        return group is >=HitGroup_t.HITGROUP_HEAD and <=HitGroup_t.HITGROUP_RIGHTLEG or HitGroup_t.HITGROUP_NECK
            ? group : HitGroup_t.HITGROUP_CHEST;
    }
    public override void Cleanup(PowerContext context) => _shot=null;
}
