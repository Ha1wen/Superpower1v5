using Superpower1v5.Support;

namespace Superpower1v5.Powers.Active;

public sealed class RandomSwap() : Power("random_swap")
{
    public override bool Activate(PowerContext context)
    {
        if (context.Pawn is not { } owner || context.Boss is not { } player) return false;
        if (!context.Plugin.Powers.IsRecovering(player))
        {
            var candidates=context.Plugin.Mode.Eligible().Where(other=>
                other.EntityHandle.Raw!=player.EntityHandle.Raw && !context.Plugin.Powers.IsRecovering(other)).ToArray();
            Random.Shared.Shuffle(candidates.AsSpan());
            foreach (var other in candidates)
            {
                if (Players.AlivePawn(other) is not { } pawn || !SafeTeleport.TrySwap(owner,pawn)) continue;
                context.Tell($"Swapped places with {other.PlayerName}.");
                return true;
            }
        }
        context.Tell("No safe player swap is available. Cooldown was not spent.");
        return false;
    }
}
