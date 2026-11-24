using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Superpower1v5.Support;
using GameTrace=CounterStrikeSharp.API.Modules.Utils.Trace;

namespace Superpower1v5.Powers.Combat;

public sealed class ThunderGod() : Power("thunder_god")
{
    // Longer than the diagonal of the playable Source world.
    private const float MapRayLength=65536;
    private static readonly TraceOptions ShotMask=new() { InteractsWith=Masks.Shot };
    private uint? _held;
    private int _lastAttack;
    private uint? _weapon;
    private uint _pawn;
    private int _readyTick;
    private int? _previousAttackTick;
    private bool _singleUse;

    internal static bool IsNativeTaserDamage(CTakeDamageInfo info)
    {
        if (info.Ability.Value is { IsValid:true } weapon) return weapon.DesignerName=="weapon_taser";
        if (info.Inflictor.Value is not { } inflictor) return false;
        if (inflictor.DesignerName=="weapon_taser") return true;
        return (info.BitsDamageType & (DamageTypes_t.DMG_SHOCK|DamageTypes_t.DMG_BULLET))!=0 &&
            inflictor.EntityHandle.Raw==info.Attacker.Raw && inflictor.DesignerName=="player" &&
            inflictor.As<CCSPlayerPawn>().WeaponServices?.ActiveWeapon.Value?.DesignerName=="weapon_taser";
    }

    internal void Discharge(PowerContext context)
    {
        if (context.Pawn?.WeaponServices?.ActiveWeapon.Value is { DesignerName:"weapon_taser" } active)
            Discharge(context,active.As<CWeaponTaser>());
    }

    public override void Apply(PowerContext context)
    {
        if (context.Pawn?.WeaponServices?.ActiveWeapon.Value is not { DesignerName:"weapon_taser" } active) return;
        _held=active.EntityHandle.Raw;
        _lastAttack=active.As<CWeaponTaser>().LastAttackTick;
    }

    public override void PostThink(PowerContext context)
    {
        // The Zeus updates this field on real discharges, including misses on game
        // paths that omit weapon_fire. Keep watching it through the automatic switch.
        if (context.Pawn is not { } pawn) return;
        var active=pawn.WeaponServices?.ActiveWeapon.Value;
        var taser=active?.DesignerName=="weapon_taser" ? active.As<CWeaponTaser>() :
            _held is { } raw ? Players.Entity<CWeaponTaser>(raw) : null;
        if (taser is null || taser.OwnerEntity.Raw!=pawn.EntityHandle.Raw) { _held=null; return; }
        var tick=taser.LastAttackTick;
        var fired=_held==taser.EntityHandle.Raw ? tick!=_lastAttack : Server.TickCount-tick is >=0 and <=2;
        if (tick>0 && fired) Discharge(context,taser);
        _held=taser.EntityHandle.Raw;
        _lastAttack=tick;
    }

    private void Discharge(PowerContext context,CWeaponTaser taser)
    {
        if (context.Pawn is not { AbsOrigin:{ } feet } pawn || context.Boss is not { } shooter) return;
        if (_weapon==taser.EntityHandle.Raw && Server.TickCount<_readyTick) return;
        ClearCharge();
        _weapon=taser.EntityHandle.Raw;
        _pawn=pawn.EntityHandle.Raw;
        _readyTick=Server.TickCount+(int)Math.Ceiling(Settings(context).Cooldown/Server.TickInterval);
        _singleUse=ConVar<float>.Find("mp_taser_recharge_time")?.Value<0;

        var origin=new Vector(feet.X+pawn.ViewOffset.X,feet.Y+pawn.ViewOffset.Y,feet.Z+pawn.ViewOffset.Z);
        var pitch=pawn.EyeAngles.X*MathF.PI/180; var yaw=pawn.EyeAngles.Y*MathF.PI/180;
        var horizontal=MapRayLength*MathF.Cos(pitch);
        var end=new Vector(origin.X+horizontal*MathF.Cos(yaw),origin.Y+horizontal*MathF.Sin(yaw),origin.Z-MapRayLength*MathF.Sin(pitch));
        var hit=GameTrace.TraceEndShape(origin,end,pawn,ShotMask);
        if (hit.DidHit() && !hit.IsAllSolid && hit.HitEntity() is { IsValid:true, Index:>0 } entity)
            context.Plugin.Damage.DealTaser(entity.As<CBaseEntity>(),shooter,pawn,taser,hit.EndPos);
    }

    public override void Tick(PowerContext context)
    {
        if (_weapon is not { } raw) return;
        if (context.Pawn is not { } owner || owner.EntityHandle.Raw!=_pawn) { ClearCharge(); return; }
        var taser=Players.Entity<CWeaponTaser>(raw);
        if (taser is not null && taser.OwnerEntity.Raw!=_pawn) { ClearCharge(); return; }
        if (Server.TickCount<_readyTick)
        {
            // A server may have a shorter global recharge. Lock only this owner's weapon.
            if (taser is not null && taser.NextPrimaryAttackTick<_readyTick)
            {
                _previousAttackTick=taser.NextPrimaryAttackTick;
                SetAttackTick(taser,_readyTick);
            }
            return;
        }
        if (taser is not null)
        {
            taser.Clip1=1;
            taser.FireTime=0;
            SetAttackTick(taser,Server.TickCount);
            Utilities.SetStateChanged(taser,"CBasePlayerWeapon","m_iClip1");
            Utilities.SetStateChanged(taser,"CWeaponTaser","m_fFireTime");
        }
        else if (_singleUse && owner.WeaponServices is { } inventory &&
            !inventory.MyWeapons.Any(handle=>handle.Value?.DesignerName=="weapon_taser"))
            context.Boss!.GiveNamedItem("weapon_taser");
        _previousAttackTick=null;
        _weapon=null;
    }

    private static void SetAttackTick(CWeaponTaser taser,int tick)
    {
        taser.NextPrimaryAttackTick=tick;
        Utilities.SetStateChanged(taser,"CBasePlayerWeapon","m_nNextPrimaryAttackTick");
    }
    private void ClearCharge()
    {
        if (_weapon is { } raw && _previousAttackTick is { } previous &&
            Players.Entity<CWeaponTaser>(raw) is { } taser && taser.NextPrimaryAttackTick==_readyTick)
            SetAttackTick(taser,previous);
        _weapon=null;
        _previousAttackTick=null;
    }
    public override void Cleanup(PowerContext context) => ClearCharge();
}
