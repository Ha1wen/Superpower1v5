using Superpower1v5.Support;

namespace Superpower1v5.Powers.Active;

public sealed class Sandevistan() : Power("sandevistan")
{
    private readonly FrozenPlayers _frozen=new();
    private double _until;
    public int FrozenCount => _frozen.Count;
    public override bool Activate(PowerContext context)
    {
        if (!context.Opponents.Any(p=>Players.AlivePawn(p) is not null)) return false;
        _until=context.Now+Settings(context).Duration;
        context.Tell($"Sandevistan: opponents frozen for {Settings(context).Duration:0.#} seconds.");
        Tick(context); return true;
    }
    public override void Tick(PowerContext context)
    {
        if (_until<=0) return;
        if (context.Now>=_until) { _until=0; _frozen.Clear(); context.Tell("Sandevistan ended."); return; }
        foreach (var opponent in context.Opponents)
            if (Players.AlivePawn(opponent) is { } pawn) _frozen.Hold(pawn,_until);
    }
    public override void PostThink(PowerContext context) => Tick(context);
    public override void Cleanup(PowerContext context) { _until=0; _frozen.Clear(); }
}
