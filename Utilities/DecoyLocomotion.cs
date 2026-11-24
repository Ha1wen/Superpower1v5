using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace Superpower1v5.Support;

internal sealed class DecoyLocomotion
{
    private const ulong WorldModelGraph=0x87CE5FF43C25BA7D;
    private const int WorldModelNodeCount=1853;
    private const int NativeGraphInstanceOffset=0x410;
    private const int GraphNodeCountOffset=0x08;
    private const int GraphNodesOffset=0x10;
    private const int ParameterValueOffset=0x18;

    // AnimGraph2 layout and entry points for Windows CS2 build 26879581.
    // Verify both modules, the graph ID and the control types before writing.
    private static readonly Lazy<(nint Animation,nint Server)> AnimationModules=new(LocateAnimationModules);
    private const int FloatNodeVTable=0x6A4338;
    private const int BoolNodeVTable=0x6A4278;
    private const int SymbolNodeVTable=0x6A42D8;
    private const int EvaluateGraph=0x11CD3F0;
    private const int PreparePoseTasks=0x11CD710;
    private const int CompletePoseTasks=0x11CD4A0;
    private const int SerializePoseRecipe=0xB41BC0;

    private readonly string _weaponName;
    private readonly string _weaponCategory;
    private AnimationSymbols? _symbols;

    private sealed record AnimationSymbols(nint Category,nint Weapon,nint Idle,nint Ground,nint Forward,nint GroundMove,nint GroundIdle);

    public DecoyLocomotion(CCSWeaponBase? weapon)
    {
        _weaponName=weapon?.DesignerName??"weapon_knife";
        _weaponCategory=weapon?.VData?.WeaponType switch
        {
            CSWeaponType.WEAPONTYPE_PISTOL or CSWeaponType.WEAPONTYPE_TASER => "weapon_category_pistol",
            CSWeaponType.WEAPONTYPE_SUBMACHINEGUN => "weapon_category_smg",
            CSWeaponType.WEAPONTYPE_RIFLE => "weapon_category_rifle",
            CSWeaponType.WEAPONTYPE_SHOTGUN => "weapon_category_shotgun",
            CSWeaponType.WEAPONTYPE_SNIPER_RIFLE => "weapon_category_sniper",
            CSWeaponType.WEAPONTYPE_MACHINEGUN => "weapon_category_machinegun",
            CSWeaponType.WEAPONTYPE_GRENADE => "weapon_category_grenade",
            CSWeaponType.WEAPONTYPE_C4 or CSWeaponType.WEAPONTYPE_EQUIPMENT => "weapon_category_equipment",
            _ => "weapon_category_knife"
        };
    }

    public bool Update(CDynamicProp prop,float speed,float scale)
    {
        var (module,server)=AnimationModules.Value;
        if (module==0 || prop.AbsOrigin is not { } origin || prop.AbsRotation is not { } angles ||
            prop.CBodyComponent?.As<CBodyComponentBaseAnimGraph>().AnimationController is not { } controller ||
            controller.PrimaryGraphId.Value!=WorldModelGraph) return false;

        var instance=Marshal.ReadIntPtr(controller.Handle,NativeGraphInstanceOffset);
        if (instance==0 || Marshal.ReadInt32(instance,GraphNodeCountOffset)!=WorldModelNodeCount) return false;
        var nodes=Marshal.ReadIntPtr(instance,GraphNodesOffset);
        if (nodes==0) return false;

        // These indices are the named controls in worldmodel.vnmgraph.
        var action=ValueAddress(nodes,0,module+SymbolNodeVTable);     // action
        var category=ValueAddress(nodes,10,module+SymbolNodeVTable);  // weapon_category
        var weapon=ValueAddress(nodes,14,module+SymbolNodeVTable);    // weapon_type
        var horizontal=ValueAddress(nodes,16,module+FloatNodeVTable); // move_speed_horizontal
        var movement=ValueAddress(nodes,18,module+SymbolNodeVTable);  // move_type
        var groundDirection=ValueAddress(nodes,30,module+SymbolNodeVTable); // ground_action_direction_id
        var direction=ValueAddress(nodes,31,module+SymbolNodeVTable); // move_direction_id
        var groundAction=ValueAddress(nodes,32,module+SymbolNodeVTable); // ground_action
        var forward=ValueAddress(nodes,34,module+FloatNodeVTable);    // move_speed_x
        var lateral=ValueAddress(nodes,35,module+FloatNodeVTable);    // move_speed_y
        var walking=ValueAddress(nodes,42,module+BoolNodeVTable);     // move_is_walking
        var previous=ValueAddress(nodes,43,module+FloatNodeVTable);  // move_speed_horizontal_previous
        if (action==0 || category==0 || weapon==0 || horizontal==0 || movement==0 ||
            groundDirection==0 || direction==0 || groundAction==0 || forward==0 ||
            lateral==0 || walking==0 || previous==0) return false;

        var symbols=_symbols??=new(MakeGlobalSymbol(_weaponCategory),MakeGlobalSymbol(_weaponName),
            MakeGlobalSymbol("action_idle"),MakeGlobalSymbol("move_type_ground"),MakeGlobalSymbol("N"),
            MakeGlobalSymbol("ground_action_move"),MakeGlobalSymbol("ground_action_idle"));
        Marshal.WriteIntPtr(action,symbols.Idle);
        Marshal.WriteIntPtr(category,symbols.Category);
        Marshal.WriteIntPtr(weapon,symbols.Weapon);
        Marshal.WriteIntPtr(movement,symbols.Ground);
        Marshal.WriteIntPtr(groundDirection,symbols.Forward);
        Marshal.WriteIntPtr(direction,symbols.Forward);
        Marshal.WriteIntPtr(groundAction,speed>0 ? symbols.GroundMove : symbols.GroundIdle);
        var speedBits=BitConverter.SingleToInt32Bits(speed);
        Marshal.WriteInt32(horizontal,speedBits);
        Marshal.WriteInt32(forward,speedBits);
        Marshal.WriteInt32(lateral,0);
        Marshal.WriteByte(walking,speed>0 ? (byte)1 : (byte)0);
        Marshal.WriteInt32(previous,speedBits);
        controller.NetworkedAnimationInputsChanged=true;
        EvaluatePose(controller.Handle,instance,server,origin,angles.Y,scale);
        return true;
    }

    private static unsafe void EvaluatePose(nint controller,nint instance,nint server,Vector origin,float yaw,float scale)
    {
        // The native transform and graph result use 16-byte aligned SIMD vectors.
        var halfYaw=yaw*MathF.PI/360;
        var memory=stackalloc byte[160];
        var transform=(float*)(((nuint)memory+15)&~(nuint)15);
        var result=(byte*)(transform+8);
        transform[0]=0;
        transform[1]=0;
        transform[2]=MathF.Sin(halfYaw);
        transform[3]=MathF.Cos(halfYaw);
        transform[4]=origin.X;
        transform[5]=origin.Y;
        transform[6]=origin.Z;
        transform[7]=scale;

        // Run the pose tasks for the server-side attachment and serialize the
        // same pose recipe the client uses to draw the player model.
        ((delegate* unmanaged<nint,byte*,float,float*,nint,nint>)(server+EvaluateGraph))(instance,result,Server.TickInterval,transform,0);
        ((delegate* unmanaged<nint,float*,void>)(server+PreparePoseTasks))(instance,transform);
        ((delegate* unmanaged<nint,void>)(server+CompletePoseTasks))(instance);
        ((delegate* unmanaged<nint,void>)(server+SerializePoseRecipe))(controller);
    }

    private static nint ValueAddress(nint nodes,int index,nint type)
    {
        var node=Marshal.ReadIntPtr(nodes,index*IntPtr.Size);
        return node!=0 && Marshal.ReadIntPtr(node)==type ? node+ParameterValueOffset : 0;
    }

    private static (nint Animation,nint Server) LocateAnimationModules()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) return default;
        using var process=Process.GetCurrentProcess();
        var modules=process.Modules.Cast<ProcessModule>().ToArray();
        var animation=modules.FirstOrDefault(module=>string.Equals(module.ModuleName,"animationsystem.dll",StringComparison.OrdinalIgnoreCase));
        var gameplayPath=Path.Combine("game","csgo","bin","win64","server.dll");
        var server=modules.FirstOrDefault(module=>module.FileName.EndsWith(gameplayPath,StringComparison.OrdinalIgnoreCase));
        if (!Matches(animation,"E666BE2327856E04E81A666C4DD1D9C0274F367317EDB15A93A4AB93DA3EA8FD") ||
            !Matches(server,"45FDDE774C52DD04A1A65D669BD47D909FB993996C31577A60EEEE07F8601C28")) return default;
        return (animation!.BaseAddress,server!.BaseAddress);
    }

    private static bool Matches(ProcessModule? module,string hash)
    {
        if (module is null) return false;
        try
        {
            using var stream=File.OpenRead(module.FileName);
            return Convert.ToHexString(SHA256.HashData(stream))==hash;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    [DllImport("tier0.dll",EntryPoint="_MakeGlobalSymbol",ExactSpelling=true)]
    private static extern nint MakeGlobalSymbol([MarshalAs(UnmanagedType.LPUTF8Str)] string value);

}
