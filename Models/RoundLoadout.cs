namespace Superpower1v5.Models;

// Engine-independent state. There is one instance for the round, never a player->powers map.
public sealed class RoundLoadout
{
    private readonly List<PowerDefinition> _powers;
    private readonly Dictionary<string, double> _readyAt = new();
    public IReadOnlyList<PowerDefinition> Powers => _powers.AsReadOnly();
    public HashSet<string> Consumed { get; } = [];

    public RoundLoadout(IEnumerable<PowerDefinition> powers)
    {
        _powers = powers.ToList();
        if (_powers.Count != 2 || !PowerCatalog.Compatible(_powers[0], _powers[1]))
            throw new ArgumentException("A loadout requires exactly two different compatible powers.");
    }
    public bool Has(string id) => _powers.Any(p => p.Id == id);
    public void SetPair(IEnumerable<PowerDefinition> powers)
    {
        var pair = powers.ToArray();
        if (pair.Length != 2 || !PowerCatalog.Compatible(pair[0], pair[1])) throw new ArgumentException("Invalid pair.");
        _powers.Clear(); _powers.AddRange(pair);
    }
    public double Remaining(string id, double now) => Math.Max(0, _readyAt.GetValueOrDefault(id) - now);
    public void StartCooldown(string id, double now, double seconds) => _readyAt[id] = now + seconds;
    public void Replace(int slot, PowerDefinition replacement)
    {
        if (slot is < 0 or > 1 || !PowerCatalog.Compatible(_powers[1 - slot], replacement))
            throw new ArgumentException("Incompatible replacement.");
        _powers[slot] = replacement;
        // Cooldowns and one-use flags survive admin rerolls and Randomizer changes.
    }
}
