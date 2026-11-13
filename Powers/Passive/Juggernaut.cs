using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Superpower1v5.Support;

namespace Superpower1v5.Powers.Passive;

public sealed class Juggernaut() : Power("juggernaut")
{
    private readonly HashSet<uint> _pawns=[];
    public override void Apply(PowerContext context)
    {
        if (context.Pawn is not { } pawn || !_pawns.Add(pawn.EntityHandle.Raw)) return;
        pawn.MaxHealth=500; pawn.Health=500;
        Utilities.SetStateChanged(pawn,"CBaseEntity","m_iMaxHealth");
        Utilities.SetStateChanged(pawn,"CBaseEntity","m_iHealth");
    }
    public override void Cleanup(PowerContext context)
    {
        foreach (var raw in _pawns)
        {
            if (Players.Entity<CCSPlayerPawn>(raw) is not { IsValid:true } pawn) continue;
            pawn.MaxHealth=100; pawn.Health=Math.Min(pawn.Health,100);
            Utilities.SetStateChanged(pawn,"CBaseEntity","m_iMaxHealth");
            Utilities.SetStateChanged(pawn,"CBaseEntity","m_iHealth");
        }
        _pawns.Clear();
    }
}
