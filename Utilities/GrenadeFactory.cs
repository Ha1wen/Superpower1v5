using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;

namespace Superpower1v5.Support;

public static class GrenadeFactory
{
    public static readonly string[] Types=["hegrenade_projectile","flashbang_projectile","smokegrenade_projectile","molotov_projectile","decoy_projectile"];
    public static bool Supported(string designer) => Array.IndexOf(Types,designer)>=0;
    // Native factories arm the internal grenade think state. Bare entities are insufficient
    // for HE/smoke/molotov. Signatures are verified in the isolated server before deployment.
    private static readonly Lazy<MemoryFunctionWithReturn<IntPtr,IntPtr,IntPtr,IntPtr,IntPtr,int,IntPtr>> He=new(()=>new(
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "48 89 5C 24 ? 48 89 6C 24 ? 48 89 74 24 ? 57 48 83 EC ? 48 8B AC 24 ? ? ? ? 49 8B F8"
            : "55 4C 89 C1 48 89 E5 41 57 49 89 FF 41 56 49 89 D6"));
    private static readonly Lazy<MemoryFunctionWithReturn<IntPtr,IntPtr,IntPtr,IntPtr,IntPtr,int,int,IntPtr>> Smoke=new(()=>new(
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "48 89 5C 24 ? 48 89 6C 24 ? 48 89 74 24 ? 57 41 56 41 57 48 83 EC ? 48 8B B4 24 ? ? ? ? 4D 8B F8"
            : "55 4C 89 C1 48 89 E5 41 57 41 56 49 89 FE 41 55 45 89 CD 41 54 4D 89 C4"));
    private static readonly Lazy<MemoryFunctionWithReturn<IntPtr,IntPtr,IntPtr,IntPtr,IntPtr,int,IntPtr>> Molotov=new(()=>new(
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "48 8B C4 48 89 58 ? 48 89 70 ? 48 89 78 ? 4C 89 40 ? 55 41 54 41 55 41 56 41 57 48 8D 6C 24"
            : "55 48 8D 05 ? ? ? ? 48 89 E5 41 57 41 56 41 55 41 54 49 89 FC 53 48 81 EC ? ? ? ? 4C 8D 35"));
    private static readonly Lazy<MemoryFunctionWithReturn<IntPtr,IntPtr,IntPtr,IntPtr,IntPtr,int,IntPtr>> Decoy=new(()=>new(
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "40 55 56 48 83 EC 58 48 89 5C 24"
            : "55 4C 89 C1 48 89 E5 41 57 45 89 CF 41 56 49 89 FE 41 55 49 89 D5 48 89 F2 48 89 FE 41 54 48 8D 3D ? ? ? ? 4D 89 C4 53 48 83 EC ? E8 ? ? ? ? 45 31 C0"));
    public static CBaseCSGrenadeProjectile? Create(string kind,Vector origin,Vector velocity,CCSPlayerPawn owner,ushort item=0)
    {
        if (!Supported(kind) || !owner.IsValid) return null;
        var angle=new QAngle(0,MathF.Atan2(velocity.Y,velocity.X)*180/MathF.PI,0);
        var spin=new Vector(600,0,0);
        var definition=item>0 ? item : kind switch { "hegrenade_projectile"=>44,"flashbang_projectile"=>43,"smokegrenade_projectile"=>45,"molotov_projectile"=>46,_=>47 };
        var pointer=kind switch {
            "hegrenade_projectile"=>He.Value.Invoke(origin.Handle,angle.Handle,velocity.Handle,spin.Handle,owner.Handle,definition),
            "smokegrenade_projectile"=>Smoke.Value.Invoke(origin.Handle,angle.Handle,velocity.Handle,spin.Handle,owner.Handle,definition,owner.TeamNum),
            "molotov_projectile"=>Molotov.Value.Invoke(origin.Handle,angle.Handle,velocity.Handle,spin.Handle,owner.Handle,definition),
            "decoy_projectile"=>Decoy.Value.Invoke(origin.Handle,angle.Handle,velocity.Handle,spin.Handle,owner.Handle,definition),
            _=>IntPtr.Zero
        };
        CBaseCSGrenadeProjectile? grenade;
        if (kind=="flashbang_projectile")
        {
            grenade=Utilities.CreateEntityByName<CBaseCSGrenadeProjectile>(kind);
            if (grenade is null) return null;
            grenade.Thrower.Raw=owner.EntityHandle.Raw; grenade.OwnerEntity.Raw=owner.EntityHandle.Raw;
            grenade.Teleport(origin,angle,velocity); grenade.DispatchSpawn();
            grenade.DetonateTime=Server.CurrentTime+1.5f; grenade.IsLive=true;
        }
        else grenade=pointer==IntPtr.Zero ? null : new CBaseCSGrenadeProjectile(pointer);
        if (grenade is not { IsValid:true }) return null;
        grenade.TeamNum=owner.TeamNum; grenade.ItemIndex=(ushort)definition;
        grenade.Thrower.Raw=owner.EntityHandle.Raw; grenade.OriginalThrower.Raw=owner.EntityHandle.Raw; grenade.OwnerEntity.Raw=owner.EntityHandle.Raw;
        grenade.InitialPosition.X=origin.X; grenade.InitialPosition.Y=origin.Y; grenade.InitialPosition.Z=origin.Z;
        grenade.InitialVelocity.X=velocity.X; grenade.InitialVelocity.Y=velocity.Y; grenade.InitialVelocity.Z=velocity.Z;
        grenade.Teleport(origin,angle,velocity);
        if (kind=="molotov_projectile") grenade.As<CMolotovProjectile>().IsIncGrenade=definition==48;
        return grenade;
    }
}
