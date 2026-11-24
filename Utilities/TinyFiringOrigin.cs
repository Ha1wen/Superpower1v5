using System.Runtime.InteropServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Superpower1v5.Powers.Movement;

namespace Superpower1v5.Support;

internal sealed class TinyFiringOrigin(Plugin plugin)
{
    // CCSWeaponBase's shot setup writes a world-space origin to its shot parameters.
    // This binding is pinned to the tested Windows CS2 server ABI.
    private const int OriginOffset=0x14;
    private const string ShotSetupSignature=
        "48 8B C4 55 41 55 41 56 48 8D A8 48 FF FF FF 48 81 EC A0 01 00 00 48 89 58 E0 4C 8B E9 48 89 70 D8 48 8D 4A 08 48 89 78 D0";
    private MemoryFunctionVoid<IntPtr,IntPtr,int>? _setup;

    public void Install()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            var setup=new MemoryFunctionVoid<IntPtr,IntPtr,int>(ShotSetupSignature);
            setup.Hook(AfterSetup,HookMode.Post);
            _setup=setup;
        }
        catch (Exception error)
        {
            plugin.Logger.LogWarning(error,"Tiny shot origin binding is unavailable on this CS2 build");
        }
    }

    public void Clear() => _setup?.Unhook(AfterSetup,HookMode.Post);

    private HookResult AfterSetup(DynamicHook hook)
    {
        if (!plugin.Config.Enabled || !plugin.Mode.Live) return HookResult.Continue;
        plugin.Guard(()=> {
            var weapon=new CCSWeaponBase(hook.GetParam<IntPtr>(0));
            if (weapon.OwnerEntity.Value is not { DesignerName:"player" } player ||
                Players.Controller(player) is not { } controller ||
                plugin.Powers.GetActive(controller).OfType<Tiny>().FirstOrDefault() is not { } tiny) return;
            var pawn=player.As<CCSPlayerPawn>();
            if (pawn.AbsOrigin is not { } feet || tiny.UnscaledEyeHeight(pawn) is not { } normalHeight) return;
            var parameters=hook.GetParam<IntPtr>(1);
            if (parameters==IntPtr.Zero) return;
            var origin=new Vector(parameters+OriginOffset);
            var scaledHeight=normalHeight*plugin.Config.Powers["tiny"].Strength;
            // Shot history can retain a standing height across a crouch transition.
            // Keep its horizontal position and direction, and use Tiny's current eye height.
            origin.Z=feet.Z+scaledHeight;
        });
        return HookResult.Continue;
    }
}
