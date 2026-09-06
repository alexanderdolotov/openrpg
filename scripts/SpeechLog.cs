using Godot;
using System.Collections.Generic;

// A shared record of who said what, where, and when — the mechanism
// that lets NPCs "hear" each other without needing direct references
// between NpcAgents (same decoupled pattern as WorldRegistry/PathGrid).
//
// Speaking never fails for lack of an audience: Say() always succeeds
// and is recorded regardless of who's around — whether it was actually
// heard is a separate, honest fact about who happened to be in range,
// not a precondition on the action. Each utterance is delivered to a
// given listener at most once (Overheard() marks it consumed for that
// listener), not re-surfaced every turn for as long as it's retained —
// retention is just cleanup for utterances nobody was around to hear.
public static class SpeechLog
{
    public const float HearingRadius = 260f; // same scale as SpatialMemory.VisionRadius — "nearby" means the same thing whether seeing or hearing
    private const float RetainSeconds = 30f; // generous: NPC turn intervals vary with LLM latency, not a fixed tick

    private class Utterance
    {
        // Named SpeakerName, not SpeakerId — every call site actually
        // passes a display name (Personality.Name, PlayerCharacter.
        // DisplayName), same Id-vs-Name convention as everywhere else
        // that's human/LLM-facing (see NpcAgent's header comment). The
        // field was just never renamed to match once that convention
        // got written down.
        public readonly string SpeakerName;
        public readonly Vector2 Position;
        public readonly string Message;
        public readonly ulong SaidAtMsec;
        public readonly HashSet<string> HeardBy = new();

        public Utterance(string speakerName, Vector2 position, string message, ulong saidAtMsec)
        {
            SpeakerName = speakerName;
            Position = position;
            Message = message;
            SaidAtMsec = saidAtMsec;
        }
    }

    private static readonly List<Utterance> _recent = new();

    public static void Say(string speakerName, Vector2 position, string message)
    {
        Prune();
        _recent.Add(new Utterance(speakerName, position, message, Time.GetTicksMsec()));
    }

    // Everything said within HearingRadius of listenerPosition that
    // this listener hasn't already been marked as having heard —
    // and marks it heard before returning, so it won't be handed back
    // to the same listener again even if still within the retention
    // window on a later call.
    public static List<(string SpeakerName, string Message)> Overheard(string listenerName, Vector2 listenerPosition)
    {
        Prune();
        var heard = new List<(string, string)>();
        foreach (Utterance u in _recent)
        {
            if (u.SpeakerName == listenerName || u.HeardBy.Contains(listenerName))
                continue;
            if (listenerPosition.DistanceTo(u.Position) <= HearingRadius)
            {
                heard.Add((u.SpeakerName, u.Message));
                u.HeardBy.Add(listenerName);
            }
        }
        return heard;
    }

    private static void Prune()
    {
        ulong now = Time.GetTicksMsec();
        ulong retainMsec = (ulong)(RetainSeconds * 1000);
        // guard against underflow in the first few seconds of a run,
        // when now < retainMsec
        ulong cutoff = now > retainMsec ? now - retainMsec : 0;
        _recent.RemoveAll(u => u.SaidAtMsec < cutoff);
    }
}
