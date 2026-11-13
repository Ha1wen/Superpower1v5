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

public sealed class FairRotation
{
    private readonly Dictionary<string, long> _lastTurn = new();
    private long _turn;
    public string Choose(IReadOnlyList<string> eligible, bool rotate, string? current, string? forced = null)
    {
        if (eligible.Count == 0) throw new InvalidOperationException("No eligible players.");
        var choice = forced is not null && eligible.Contains(forced) ? forced :
            !rotate && current is not null && eligible.Contains(current) ? current :
            eligible.OrderBy(id => _lastTurn.GetValueOrDefault(id, long.MinValue)).First();
        _lastTurn[choice] = ++_turn;
        return choice;
    }
    public void Clear() { _lastTurn.Clear(); _turn = 0; }
}
