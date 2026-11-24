using CounterStrikeSharp.API.Core;
using Superpower1v5.Support;

namespace Superpower1v5.Powers.Movement;

public sealed class Tiny() : Power("tiny")
{
    private readonly PlayerScale _scale=new();
    public override void Apply(PowerContext context) => Tick(context);
    public override void Tick(PowerContext context)
    { if (context.Pawn is { } pawn) _scale.Apply(pawn,Settings(context).Strength); }
    internal float? UnscaledEyeHeight(CCSPlayerPawn pawn) => _scale.UnscaledEyeHeight(pawn);
    public override void PostThink(PowerContext context) => Tick(context);
    public override void Cleanup(PowerContext context) => _scale.Clear();
}
