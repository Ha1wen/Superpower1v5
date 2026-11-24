using CounterStrikeSharp.API.Modules.Timers;
using Superpower1v5.Powers;
using Superpower1v5.Powers.Passive;

namespace Superpower1v5.Support;

internal static class GrenadeRefund
{
    public static void Schedule(PowerContext context,Power source,string weapon)
    {
        var kind=weapon.StartsWith("weapon_",StringComparison.Ordinal) ? weapon[7..] : weapon;
        if (kind is not ("hegrenade" or "flashbang" or "smokegrenade" or "molotov" or "incgrenade" or "decoy") ||
            context.Pawn is not { } pawn) return;
        var pawnHandle=pawn.EntityHandle.Raw;
        var generation=context.Plugin.Mode.Generation;
        // Let the engine finish consuming the original before granting the replacement.
        var delay=source is InfiniteAmmo ? 1f : 30f;
        context.Plugin.AddTimer(delay,()=>context.Plugin.Guard(()=> {
            if (generation!=context.Plugin.Mode.Generation || context.Pawn?.EntityHandle.Raw!=pawnHandle ||
                !context.Contains(source) || !context.Plugin.Mode.CanUsePlayerPowers(context.Boss)) return;
            context.Boss!.GiveNamedItem("weapon_"+kind);
        }),TimerFlags.STOP_ON_MAPCHANGE);
    }
}
