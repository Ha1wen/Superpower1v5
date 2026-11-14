using Superpower1v5.Configuration;
using Superpower1v5.Models;

namespace Superpower1v5.Managers;

public sealed class SelectionEngine(Random random)
{
    public List<PowerDefinition> SelectPair(PluginConfig config)
    {
        var pool = PowerCatalog.All.Where(config.IsAvailable).ToList();
        var first = Pick(pool.Where(a => pool.Any(b => PowerCatalog.Compatible(a, b))).ToList());
        return [first, Pick(pool.Where(b => PowerCatalog.Compatible(first, b)).ToList())];
    }

    public PowerDefinition Replacement(PowerDefinition kept, IEnumerable<string> excluded, PluginConfig config)
    {
        var skip = excluded.ToHashSet();
        return Pick(PowerCatalog.All.Where(p => config.IsAvailable(p) && !skip.Contains(p.Id) &&
            PowerCatalog.Compatible(kept, p)).ToList());
    }

    public List<PowerDefinition> SelectDifferentPair(PluginConfig config,IReadOnlyList<PowerDefinition>? previous)
    {
        if (previous is null) return SelectPair(config);
        var pool=PowerCatalog.All.Where(config.IsAvailable).ToArray();
        var old=previous.Select(p=>p.Id).ToHashSet();
        var pairs=new List<List<PowerDefinition>>();
        for (var a=0;a<pool.Length;a++) for (var b=a+1;b<pool.Length;b++)
            if (PowerCatalog.Compatible(pool[a],pool[b]) && !(old.Contains(pool[a].Id) && old.Contains(pool[b].Id)))
                pairs.Add([pool[a],pool[b]]);
        if (pairs.Count==0) return SelectPair(config); // A two-power configuration has only one pair.
        var pair=pairs[random.Next(pairs.Count)];
        if (random.Next(2)==1) pair.Reverse();
        return pair;
    }

    private PowerDefinition Pick(List<PowerDefinition> pool)
    {
        if (pool.Count == 0) throw new InvalidOperationException("Enable at least two different compatible powers.");
        return pool[random.Next(pool.Count)];
    }
}
