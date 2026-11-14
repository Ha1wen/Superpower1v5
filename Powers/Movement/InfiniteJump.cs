using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace Superpower1v5.Powers.Movement;

public sealed class InfiniteJump() : Power("infinite_jump")
{
    private bool _held;
    public override void Apply(PowerContext c) => _held=c.Boss is { } p && (p.Buttons&PlayerButtons.Jump)!=0;
    public override void Tick(PowerContext c)
    {
        if (c.Boss is not { } player || c.Pawn is not { } pawn) return;
        var pressed=(player.Buttons&PlayerButtons.Jump)!=0;
        var rising=pressed && !_held;
        _held=pressed;
        if (!rising || (pawn.Flags&(uint)PlayerFlags.FL_ONGROUND)!=0 || pawn.ActualMoveType!=MoveType_t.MOVETYPE_WALK) return;
        pawn.GroundEntity.Raw=uint.MaxValue;
        pawn.Teleport(null,null,new Vector(pawn.AbsVelocity.X,pawn.AbsVelocity.Y,Settings(c).Strength));
        c.Plugin.Powers.Noise(player);
    }
    public override void Cleanup(PowerContext c) => _held=false;
}
