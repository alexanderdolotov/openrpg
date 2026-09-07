using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// A rolling record of everything this NPC has perceived or done —
// location history, thoughts, actions taken, and (once dialogue exists)
// conversations. Grows every turn; once the raw log would blow past
// MaxLlmChars in a prompt, Compress() makes one LLM call to fold it
// into a short diary paragraph and drops the raw entries that fed it.
// The mind only ever sees Render(): the diary plus whatever's
// accumulated since the last compression.
//
// Owned per-NPC — each NpcAgent holds its own NpcMemory (see NpcAgent's
// Memory property), never shared, same as its own Mind.
public class NpcMemory
{
    // Budget for how much raw memory is allowed to accumulate before a
    // compression pass runs — not a hard cap on the whole class, just
    // what's allowed to ride along in a single prompt.
    public const int MaxLlmChars = 1200;

    private readonly List<string> _entries = new();
    private int _turn = 0;

    public string Diary { get; private set; } = "";

    /// kind is a free-form tag, not a restricted enum — Render() below
    /// includes every entry regardless of kind, so callers just pick
    /// whatever short word actually describes the entry (in use as of
    /// this writing: location, thought, action, speech, heard,
    /// witnessed, inventory, discovery, steal, trade, emotion).
    public void Record(string kind, string text)
    {
        _turn++;
        _entries.Add($"turn {_turn} [{kind}] {text}");
    }

    public bool NeedsCompression => RawLength() > MaxLlmChars;

    public string Render()
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(Diary))
            parts.Add($"What you remember overall: {Diary}");
        if (_entries.Count > 0)
            parts.Add("Recently:\n" + string.Join("\n", _entries));
        return parts.Count > 0 ? string.Join("\n\n", parts) : "You don't remember anything yet.";
    }

    public async Task CompressIfNeeded(Mind mind)
    {
        if (!NeedsCompression)
            return;

        string raw = string.Join("\n", _entries);
        Diary = await mind.Summarize(Diary, raw);
        _entries.Clear();
    }

    private int RawLength() => Diary.Length + _entries.Sum(e => e.Length + 1);
}
