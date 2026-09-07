using Godot;
using System.Collections.Generic;

// A shared record of who said what, where, and when — the mechanism
// that lets NPCs "hear" each other without needing direct references
// between NpcAgents (same decoupled pattern as WorldRegistry/PathGrid).
// Thin now — the actual buffering/consumption mechanics live in
// RecentEventBuffer, shared with WorldEventLog (visible actions); this
// class is just the audible-specific naming and HearingRadius.
public static class SpeechLog
{
    public const float HearingRadius = 260f; // same scale as SpatialMemory.VisionRadius — "nearby" means the same thing whether seeing or hearing
    private const float RetainSeconds = 30f; // generous: NPC turn intervals vary with LLM latency, not a fixed tick

    private static readonly RecentEventBuffer _buffer = new(RetainSeconds);

    public static void Say(string speakerName, Vector2 position, string message) =>
        _buffer.Record(speakerName, position, message);

    // Everything said within HearingRadius of listenerPosition that
    // this listener hasn't already been marked as having heard — see
    // RecentEventBuffer.Consume() for the "at most once per listener"
    // mechanics.
    public static List<(string SpeakerName, string Message)> Overheard(string listenerName, Vector2 listenerPosition) =>
        _buffer.Consume(listenerName, listenerPosition, HearingRadius);

    // Called once at boot (Main._Ready(), alongside WorldEventLog's own
    // and WorldExploration's) — static state like _buffer survives a
    // "Restart Game" scene reload (autoloads and static classes both
    // live outside the scene tree the reload actually tears down), so
    // without this, a restarted session could briefly overhear a stale
    // line from whoever happened to be speaking right before the old
    // session ended, said by a "speaker" that no longer exists in the
    // new one.
    public static void Reset() => _buffer.Clear();
}
