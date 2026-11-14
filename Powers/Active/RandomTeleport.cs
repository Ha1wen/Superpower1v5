using Superpower1v5.Support;

namespace Superpower1v5.Powers.Active;

public sealed class RandomTeleport() : Power("random_teleport")
{
    public override bool Activate(PowerContext c)
    {
        if (c.Plugin.Powers.IsRecovering(c.Boss)) { c.Tell("Wait for Second Chance to finish rewinding."); return false; }
        if (c.Pawn is { } pawn && SafeTeleport.TryRandom(pawn)) return true;
        c.Tell("No safe random destination was found. Cooldown was not spent.");
        return false;
    }
}
