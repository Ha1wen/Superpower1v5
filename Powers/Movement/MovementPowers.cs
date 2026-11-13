namespace Superpower1v5.Powers.Movement;

public sealed class SuperSpeed() : Power("super_speed")
{
    public override void Apply(PowerContext context) => Tick(context);
    public override void Tick(PowerContext context)
    {
        if (context.Pawn is { } pawn)
            context.Plugin.Effects.Speed(pawn, Id, Settings(context).Strength);
    }
}

public sealed class LowGravity() : Power("low_gravity")
{
    public override void Apply(PowerContext context) => Tick(context);
    public override void Tick(PowerContext context)
    {
        // CS2 can recalculate its actual gravity during player simulation. Refresh both fields.
        if (context.Pawn is { } pawn)
            context.Plugin.Effects.Gravity(pawn, Id, Settings(context).Strength);
    }
}
