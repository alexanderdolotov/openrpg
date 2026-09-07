using Godot;
using System.Collections.Generic;

// The shared "who did/said what, where, and who's already caught up on
// it" mechanism SpeechLog (audible) and WorldEventLog (visible) both
// need — identical shape, just a different radius and a different verb
// at each call site. Factored out here once both needed it, rather
// than copy-pasting SpeechLog's original buffering logic a second time
// for actions.
//
// This is also THE actual answer to "won't broadcasting every action
// spam the LLM with everything happening at once?" — nothing here ever
// pushes an event INTO an NPC's context the instant it happens. An
// entry just sits here, consumed at most once per listener, until that
// listener's own next turn comes around and asks what it missed —
// which already only happens every few seconds (NpcAgent.MinTurnPause
// plus real LLM latency, unchanged by any of this). Multiple things
// happening between two of an NPC's turns all land in ONE batched
// perception update when that turn finally comes, not one interruption
// per event — the exact "rolling context, sent every few seconds"
// shape asked for, for free, from a mechanism that already existed.
internal class RecentEventBuffer
{
    private class Entry
    {
        public readonly string ActorName;
        public readonly Vector2 Position;
        public readonly string Description;
        public readonly ulong AtMsec;

        // null = anyone within range can consume this, same as every
        // entry always worked before. Non-null restricts it to exactly
        // those listener names — used for something only a few
        // specific bystanders ever get a chance to notice at all (see
        // WorldEventLog's Wisdom-vs-stealth handling for steal), rather
        // than something genuinely public everyone nearby sees.
        public readonly HashSet<string> RestrictTo;
        public readonly HashSet<string> ConsumedBy = new();

        public Entry(string actorName, Vector2 position, string description, ulong atMsec, HashSet<string> restrictTo)
        {
            ActorName = actorName;
            Position = position;
            Description = description;
            AtMsec = atMsec;
            RestrictTo = restrictTo;
        }
    }

    private readonly List<Entry> _recent = new();
    private readonly float _retainSeconds;

    // retainSeconds: how long an entry nobody's consumed yet sticks
    // around before it's just cleanup (not a promise anyone will ever
    // see it) — generous on purpose, since NPC turn intervals vary with
    // LLM latency, not a fixed tick.
    public RecentEventBuffer(float retainSeconds)
    {
        _retainSeconds = retainSeconds;
    }

    // Recording never fails for lack of an audience — whether anyone
    // was actually around to notice is a separate, honest fact decided
    // at Consume() time, not a precondition here.
    public void Record(string actorName, Vector2 position, string description, IEnumerable<string> restrictTo = null)
    {
        Prune();
        HashSet<string> restrict = restrictTo != null ? new HashSet<string>(restrictTo) : null;
        _recent.Add(new Entry(actorName, position, description, Time.GetTicksMsec(), restrict));
    }

    // Everything within `radius` of listenerPosition that this listener
    // hasn't already consumed (and isn't restricted away from) — marks
    // each one consumed before returning, so it's handed back at most
    // once per listener, ever, not re-surfaced every turn for as long
    // as it's retained.
    public List<(string ActorName, string Description)> Consume(string listenerName, Vector2 listenerPosition, float radius)
    {
        Prune();
        var result = new List<(string, string)>();
        foreach (Entry e in _recent)
        {
            if (e.ActorName == listenerName || e.ConsumedBy.Contains(listenerName))
                continue;
            if (e.RestrictTo != null && !e.RestrictTo.Contains(listenerName))
                continue;
            if (listenerPosition.DistanceTo(e.Position) <= radius)
            {
                result.Add((e.ActorName, e.Description));
                e.ConsumedBy.Add(listenerName);
            }
        }
        return result;
    }

    // For SpeechLog.Reset()/WorldEventLog.Reset() — see their own
    // comments for why this needs to exist at all (a static buffer, so
    // it otherwise survives a scene reload the "Restart Game" button
    // triggers, well past the point where it stops meaning anything).
    public void Clear() => _recent.Clear();

    private void Prune()
    {
        ulong now = Time.GetTicksMsec();
        ulong retainMsec = (ulong)(_retainSeconds * 1000);
        // guard against underflow in the first few seconds of a run,
        // when now < retainMsec
        ulong cutoff = now > retainMsec ? now - retainMsec : 0;
        _recent.RemoveAll(e => e.AtMsec < cutoff);
    }
}
