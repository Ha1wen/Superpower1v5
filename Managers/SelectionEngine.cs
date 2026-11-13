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

    private PowerDefinition Pick(List<PowerDefinition> pool)
    {
        if (pool.Count == 0) throw new InvalidOperationException("Enable at least two different compatible powers.");
        return pool[random.Next(pool.Count)];
    }
}
