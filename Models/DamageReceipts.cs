namespace Superpower1v5.Models;

// Match the event and native-post views of a hit, whichever arrives first.
// Multiple pellets/hits in one tick stay distinct because the remaining health differs.
public sealed class DamageReceipts
{
    private sealed record Receipt(int Tick,uint Attacker,int Damage,int HealthAfter,bool FromEvent);
    private readonly Dictionary<uint,List<Receipt>> _receipts=new();
    public bool Accept(uint victim,uint attacker,int tick,int damage,int healthAfter,bool fromEvent)
    {
        if (!_receipts.TryGetValue(victim,out var receipts)) _receipts[victim]=receipts=[];
        receipts.RemoveAll(r=>r.Tick!=tick);
        var match=receipts.FindIndex(r=>r.Attacker==attacker && r.Damage==damage && r.HealthAfter==healthAfter && r.FromEvent!=fromEvent);
        if (match>=0) { receipts.RemoveAt(match); return false; }
        receipts.Add(new(tick,attacker,damage,healthAfter,fromEvent));
        return true;
    }
    public void Clear() => _receipts.Clear();
}
