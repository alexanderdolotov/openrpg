using System.Collections.Generic;

// A multi-item carry state shared by every character in the world — NPC
// or player, no distinction, since both are an NPCActor underneath (see
// NPCActor.Inventory). Counts, not slots: "2 apples, 1 fish" rather than
// one item at a time. Deliberately no capacity cap — the interesting
// behavior lives in how items MOVE between inventories (deposit, trade,
// steal), not in how many any one inventory can hold.
public class Inventory
{
    private readonly Dictionary<string, int> _counts = new();

    // Exposed read-only — callers mutate through Add()/Remove() so the
    // count invariant (never negative, zero entries pruned) can't be
    // violated from outside.
    public IReadOnlyDictionary<string, int> All => _counts;

    public int Count(string item) => _counts.TryGetValue(item, out int n) ? n : 0;
    public bool Has(string item, int amount = 1) => Count(item) >= amount;

    public void Add(string item, int amount = 1)
    {
        if (amount <= 0) return;
        _counts[item] = Count(item) + amount;
    }

    // False (and no change made) if there isn't enough — callers check
    // the result rather than needing a separate Has() pre-check.
    public bool Remove(string item, int amount = 1)
    {
        if (amount <= 0 || !Has(item, amount))
            return false;
        int remaining = Count(item) - amount;
        if (remaining <= 0)
            _counts.Remove(item);
        else
            _counts[item] = remaining;
        return true;
    }

    // Snapshot for before/after comparisons (noticing a loss or gain
    // caused by someone else) — a plain copy, not a live view.
    public Dictionary<string, int> Snapshot() => new(_counts);

    public string Describe()
    {
        if (_counts.Count == 0)
            return "nothing";
        var parts = new List<string>();
        foreach (KeyValuePair<string, int> kv in _counts)
            parts.Add($"{kv.Value} {kv.Key}" + (kv.Value != 1 ? "s" : ""));
        return string.Join(", ", parts);
    }
}
