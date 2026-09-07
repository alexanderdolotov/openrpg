using System.Collections.Generic;

// Notices an inventory change caused by someone ELSE — a trade
// received, an item stolen — by comparing against a baseline snapshot.
// This is the whole mechanism behind "wonder if an item was lost or
// stolen": nothing anywhere flags "you were robbed" directly — a
// character just notices its own count doesn't match what it remembers
// and has to decide what that means, same as a person patting their
// pockets.
//
// Shared between NpcAgent (wired into Memory and its own turn loop) and
// PlayerCharacter (wired into nothing but its own per-frame refresh) —
// before this existed, both carried their own copy of the exact same
// comparison and near-identical wording, purely because one has a
// Memory to record into and the other doesn't. That difference lives in
// what the CALLER does with the returned notes, not in the comparison
// itself, so only the comparison needed to move here.
public class InventoryWatcher
{
    private Dictionary<string, int> _baseline = new();

    // Call this right after the character's OWN action resolves —
    // absorbs whatever that action did to the inventory into the
    // baseline, so the next DetectExternalChanges() call can only ever
    // attribute a difference to someone ELSE's doing. Skipping this call
    // (or calling it late) is what would let a character's own gather/
    // deposit/trade get misread as "someone gave or took something."
    public void AbsorbOwnChange(Inventory inventory)
    {
        _baseline = inventory.Snapshot();
    }

    // Call this once per turn/frame, before anything else could change
    // the inventory. Returns one human-readable note per item that
    // changed since the last AbsorbOwnChange() — empty if nothing did.
    // Also refreshes the baseline itself, so a detected change is only
    // ever reported once, not on every subsequent call.
    public List<string> DetectExternalChanges(Inventory inventory)
    {
        var notes = new List<string>();

        foreach (KeyValuePair<string, int> before in _baseline)
        {
            int after = inventory.Count(before.Key);
            if (after < before.Value)
            {
                int missing = before.Value - after;
                notes.Add($"You notice you're missing {missing} {before.Key}{(missing != 1 ? "s" : "")} you had before — you didn't deposit, give, or trade it away, so it must have been taken or lost.");
            }
        }

        foreach (KeyValuePair<string, int> after in inventory.All)
        {
            int before = _baseline.TryGetValue(after.Key, out int b) ? b : 0;
            if (after.Value > before)
            {
                int gained = after.Value - before;
                notes.Add($"You notice you now have {gained} more {after.Key}{(gained != 1 ? "s" : "")} than before — someone must have given or traded it to you.");
            }
        }

        _baseline = inventory.Snapshot();
        return notes;
    }
}
