using Superpower1v5.Support;

namespace Superpower1v5.Powers.Active;

public sealed class RandomKill(Random? random=null) : Power("random_explosion")
{
    private readonly Random _random=random ?? Random.Shared;
    public override bool Activate(PowerContext context)
    {
        if (context.Pawn is null) return false;
        var candidates=context.Plugin.Mode.Eligible().Where(p=>Players.AlivePawn(p) is not null).ToArray();
        if (candidates.Length==0) return false;
        var target=candidates[_random.Next(candidates.Length)];
        if (Players.AlivePawn(target) is not { } pawn) return false;
        context.Tell($"Random Kill selected {target.PlayerName}!");
        pawn.CommitSuicide(explode:false,force:true);
        return true;
    }
}
