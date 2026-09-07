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
}
