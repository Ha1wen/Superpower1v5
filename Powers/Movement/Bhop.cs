using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace Superpower1v5.Powers.Movement;

public sealed class Bhop() : Power("bhop")
{
    private double _lastHop=-1;
    public override void Tick(PowerContext context)
    {
        if (context.Pawn is not { } pawn || context.Boss is not { } boss || context.Now-_lastHop<.08 ||
            (pawn.Flags&(uint)PlayerFlags.FL_ONGROUND)==0 || (boss.Buttons&PlayerButtons.Jump)==0 ||
            pawn.ActualMoveType!=MoveType_t.MOVETYPE_WALK) return;
        _lastHop=context.Now;
        pawn.GroundEntity.Raw=uint.MaxValue; pawn.Flags&=~(uint)PlayerFlags.FL_ONGROUND;
        pawn.Teleport(null,null,new Vector(pawn.AbsVelocity.X,pawn.AbsVelocity.Y,Settings(context).Strength));
    }
}
