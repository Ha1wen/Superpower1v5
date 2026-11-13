using System.Runtime.InteropServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
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
#if SERVER_SMOKE
    public void NativeProbeHit(uint target,uint source,float amount,DamageTypes_t type=DamageTypes_t.DMG_BULLET) => ApplyDamage(target,source,amount,type,false);
#endif
    private unsafe void ApplyDamage(uint targetRaw,uint sourceRaw,float amount,DamageTypes_t type,bool secondary)
    {
        if (!plugin.Mode.Live || _applying || !float.IsFinite(amount) || amount<=0) return;
        var target=Players.Resolve(targetRaw); var source=Players.Resolve(sourceRaw);
        if (Players.AlivePawn(target) is not { } victim || source?.PlayerPawn.Value is not { IsValid:true } attacker || target!.TeamNum==source.TeamNum) return;
        ApplyEngineDamage(victim,source,attacker,amount,type,secondary);
    }
    private unsafe void ApplyEngineDamage(CBaseEntity victim,CCSPlayerController source,CCSPlayerPawn attacker,float amount,DamageTypes_t type,bool secondary)
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
            info.Attacker.Raw=attacker.EntityHandle.Raw; info.Inflictor.Raw=attacker.EntityHandle.Raw; info.Ability.Raw=uint.MaxValue;
            info.Damage=amount; info.OriginalDamage=amount; info.BitsDamageType=type;
            Schema.SetSchemaValue(result.Handle,"CTakeDamageResult","m_pOriginatingInfo",info.Handle);
            result.HealthBefore=victim.Health; result.HealthLost=(int)amount; result.DamageDealt=amount;
            result.PreModifiedDamage=amount; result.TotalledHealthLost=(int)amount; result.TotalledDamageDealt=amount;
            _applying=true; Synthetic=secondary; SourcePawn=attacker.EntityHandle.Raw; TargetPawn=victim.EntityHandle.Raw;
            VirtualFunctions.CBaseEntity_TakeDamageOld(victim,info,result);
        }
        finally
        {
            _applying=false; Synthetic=false; SourcePawn=null; TargetPawn=null;
            NativeMemory.Free(infoMemory); NativeMemory.Free(resultMemory);
        }
    }
}
