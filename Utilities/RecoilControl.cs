using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace Superpower1v5.Support;

public sealed class RecoilControl
{
    private uint? _viewer;
    private static readonly string[] ClientVars=["weapon_recoil_scale","weapon_recoil_view_punch_extra"];
    public void Apply(CCSPlayerController player)
    {
        if (_viewer==player.EntityHandle.Raw) return;
        Clear(); _viewer=player.EntityHandle.Raw;
        if (!player.IsBot) foreach (var name in ClientVars)
            if (ConVar<float>.Find(name) is not null) player.ReplicateConVar(name,"0");
    }
    public static void Reset(CCSPlayerPawn pawn)
    {
        if (pawn.AimPunchServices is { } punch)
        {
            Zero(punch.PredictableBaseAngle); Zero(punch.PredictableBaseAngleVel); Zero(punch.UnpredictableBaseAngle);
            punch.PredictableBaseTick=Server.TickCount; punch.PredictableBaseTickInterpAmount=0; punch.UnpredictableBaseTick=Server.TickCount;
            foreach (var field in new[] { "m_predictableBaseAngle","m_predictableBaseAngleVel","m_unpredictableBaseAngle","m_predictableBaseTick","m_predictableBaseTickInterpAmount","m_unpredictableBaseTick" })
                Dirty(punch,"CCSPlayer_AimPunchServices",field);
        }
        if (pawn.CameraServices is { } camera)
        {
            Zero(camera.CsViewPunchAngle); camera.CsViewPunchAngleTick=Server.TickCount; camera.CsViewPunchAngleTickRatio=0;
            foreach (var field in new[] { "m_vecCsViewPunchAngle","m_nCsViewPunchAngleTick","m_flCsViewPunchAngleTickRatio" }) Dirty(camera,"CPlayer_CameraServices",field);
        }
        if (pawn.WeaponServices?.ActiveWeapon.Value is { } active && Players.Firearm(active))
        {
            var weapon=active.As<CCSWeaponBase>();
            weapon.FlRecoilIndex=0; weapon.IRecoilIndex=0;
            if (Schema.IsSchemaFieldNetworked("CCSWeaponBase","m_flRecoilIndex")) Utilities.SetStateChanged(weapon,"CCSWeaponBase","m_flRecoilIndex");
        }
    }
    private static void Dirty(CPlayerPawnComponent component,string type,string field)
    {
        if (Schema.IsSchemaFieldNetworked(type,field))
            NativeAPI.SchemaNetworkStateChanged(component.__m_pChainEntity.Handle,(uint)Schema.GetSchemaOffset(type,field),uint.MaxValue,uint.MaxValue);
    }
    private static void Zero(QAngle angle) { angle.X=0; angle.Y=0; angle.Z=0; }
    public void Clear()
    {
        if (_viewer is { } raw && Players.Resolve(raw) is { IsBot:false } player)
            foreach (var name in ClientVars) if (ConVar<float>.Find(name) is { } cvar) player.ReplicateConVar(name,cvar.ValueAsString);
        _viewer=null;
    }
}
