using System.Runtime.InteropServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using Superpower1v5.Models;
using Superpower1v5.Support;

namespace Superpower1v5.Managers;

public sealed class DamageService(Plugin plugin)
{
    public static bool HasResult(CTakeDamageInfo? info, CTakeDamageResult? result) =>
        info is not null && info.Handle != IntPtr.Zero && result is not null && result.Handle != IntPtr.Zero;
    public bool Synthetic { get; private set; }
    public uint? SourcePawn { get; private set; }
    public uint? TargetPawn { get; private set; }
    private bool _applying;
    // API 380's Native/ListenerTests uses this three-argument engine binding. Allocation sizes
    // come from schema. AttackerInfo is an unschematized field at 0x88 in this pinned API ABI.
    [StructLayout(LayoutKind.Sequential)]
    private struct AttackerInfo
    {
        public byte NeedInit,IsPawn,IsWorld,Padding;
        public uint Pawn;
        public int Slot,TeamChecked,Team;
    }
    public void Deal(uint targetRaw,uint sourceRaw,float amount,DamageTypes_t type=DamageTypes_t.DMG_GENERIC) => ApplyDamage(targetRaw,sourceRaw,amount,type,true);
    public void DealTaser(CBaseEntity target,CCSPlayerController source,CCSPlayerPawn attacker,CWeaponTaser taser,Vector impact)
    {
        if (!plugin.Mode.Live || _applying || !target.IsValid || target.Index==0 || !target.TakesDamage ||
            taser.VData is not { Damage:>0 } data) return;
        if (Players.Controller(target) is { } player && !plugin.Mode.Enemies(source,player)) return;
        ApplyEngineDamage(target,source,attacker,data.Damage,DamageTypes_t.DMG_SHOCK,true,HitGroup_t.HITGROUP_GENERIC,
            impact,weapon:taser.EntityHandle.Raw);
    }
    public void DealBullet(uint targetRaw,uint sourceRaw,float amount,HitGroup_t group,Vector? impact=null) =>
        ApplyDamage(targetRaw,sourceRaw,amount,DamageTypes_t.DMG_BULLET,true,group,impact);
#if SERVER_SMOKE
    public void NativeProbeHit(uint target,uint source,float amount,DamageTypes_t type=DamageTypes_t.DMG_BULLET,HitGroup_t group=HitGroup_t.HITGROUP_INVALID)
        => ApplyDamage(target,source,amount,type,false,group);
#endif
    private void ApplyDamage(uint targetRaw,uint sourceRaw,float amount,DamageTypes_t type,bool secondary,
        HitGroup_t group=HitGroup_t.HITGROUP_INVALID,Vector? impact=null)
    {
        if (!plugin.Mode.Live || _applying || !float.IsFinite(amount) || amount<=0) return;
        var target=Players.Resolve(targetRaw); var source=Players.Resolve(sourceRaw);
        if (Players.AlivePawn(target) is not { } victim || source?.PlayerPawn.Value is not { IsValid:true } attacker || !plugin.Mode.Enemies(target!,source)) return;
        if (secondary && type==DamageTypes_t.DMG_BULLET && group!=HitGroup_t.HITGROUP_INVALID)
        {
            var weapon=attacker.WeaponServices?.ActiveWeapon.Value?.As<CCSWeaponBase>();
            var vdata=weapon?.VData;
            var part=group==HitGroup_t.HITGROUP_HEAD ? "head" : "body";
            var team=victim.TeamNum==2 ? "t" : "ct";
            var scale=ConVar<float>.Find($"mp_damage_scale_{team}_{part}")?.Value ?? 1;
            var helmet=victim.ItemServices?.As<CCSPlayer_ItemServices>().HasHelmet==true;
            var (health,armor)=BulletHealthDamage.Split(amount,group,vdata?.HeadshotMultiplier??4,scale,
                victim.ArmorValue,helmet,vdata?.ArmorRatio??1);
            ApplyEngineDamage(victim,source,attacker,health,type,secondary,group,impact??victim.AbsOrigin,
                (int)MathF.Ceiling(armor),weapon?.EntityHandle.Raw);
        }
        else ApplyEngineDamage(victim,source,attacker,amount,type,secondary,group);
    }
    private unsafe void ApplyEngineDamage(CBaseEntity victim,CCSPlayerController source,CCSPlayerPawn attacker,
        float amount,DamageTypes_t type,bool secondary,HitGroup_t group,Vector? impact=null,int spentArmor=0,uint? weapon=null)
    {
        var infoSize=Schema.GetClassSize("CTakeDamageInfo"); var resultSize=Schema.GetClassSize("CTakeDamageResult");
        if (infoSize<0x88+sizeof(AttackerInfo) || infoSize>4096 || resultSize<16 || resultSize>4096)
            throw new InvalidOperationException("Unsupported engine damage structure layout; requires the API 380 ABI.");
        var infoMemory=NativeMemory.AllocZeroed((nuint)infoSize);
        var resultMemory=NativeMemory.AllocZeroed((nuint)resultSize);
        try
        {
            var info=new CTakeDamageInfo((nint)infoMemory); var result=new CTakeDamageResult((nint)resultMemory);
            *(AttackerInfo*)((byte*)infoMemory+0x88)=new() { NeedInit=1,IsPawn=1,Pawn=attacker.EntityHandle.Raw,Slot=source.Slot,Team=source.TeamNum };
            info.Attacker.Raw=attacker.EntityHandle.Raw; info.Inflictor.Raw=attacker.EntityHandle.Raw; info.Ability.Raw=weapon??uint.MaxValue;
            info.Damage=amount; info.OriginalDamage=amount; info.BitsDamageType=type;
            info.HitGroupId=group;
            if (impact is not null)
            {
                // The caller provides the final health damage for this traced hit.
                info.DamageFlags|=TakeDamageFlags_t.DFLAG_IGNORE_ARMOR|TakeDamageFlags_t.DFLAG_SUPPRESS_PHYSICS_FORCE;
                info.DamagePosition.X=impact.X; info.DamagePosition.Y=impact.Y; info.DamagePosition.Z=impact.Z;
            }
            Schema.SetSchemaValue(result.Handle,"CTakeDamageResult","m_pOriginatingInfo",info.Handle);
            result.HealthBefore=victim.Health; result.HealthLost=(int)amount; result.DamageDealt=amount;
            result.PreModifiedDamage=amount; result.TotalledHealthLost=(int)amount; result.TotalledDamageDealt=amount;
            _applying=true; Synthetic=secondary; SourcePawn=attacker.EntityHandle.Raw; TargetPawn=victim.EntityHandle.Raw;
            VirtualFunctions.CBaseEntity_TakeDamageOld(victim,info,result);
            if (spentArmor>0 && info.Damage>0 && result.HealthLost>0 && victim.IsValid)
            {
                var pawn=victim.As<CCSPlayerPawn>();
                pawn.ArmorValue=Math.Max(0,pawn.ArmorValue-spentArmor);
                CounterStrikeSharp.API.Utilities.SetStateChanged(victim,"CCSPlayerPawn","m_ArmorValue");
            }
        }
        finally
        {
            _applying=false; Synthetic=false; SourcePawn=null; TargetPawn=null;
            NativeMemory.Free(infoMemory); NativeMemory.Free(resultMemory);
        }
    }
}
