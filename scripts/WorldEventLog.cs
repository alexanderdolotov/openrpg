using Godot;
using System.Collections.Generic;

// SpeechLog's counterpart for things a nearby character would actually
// SEE, not hear — someone gathering, giving an item away, arriving
// somewhere, lying down to sleep. Same shared RecentEventBuffer
// mechanics as SpeechLog (buffered, consumed at most once per witness,
// only surfaced when that witness's own turn comes around — see
// RecentEventBuffer's header for why that's what keeps this from
// "spamming" anyone).
//
// steal is deliberately never Announce()'d here the normal way — it's
// the one action that's supposed to stay hidden. AnnounceStealthAttempt()
// is its own, narrower path: nearby bystanders each get their own
// Wisdom-vs-the-thief's-Dexterity roll, and only whoever's roll
// succeeds ever has a chance to find out, via RecentEventBuffer's
// RestrictTo — everyone else genuinely never sees it, same as the
// victim, who still only ever finds out from noticing their own
// inventory came up short (unchanged, unrelated to this).
public static class WorldEventLog
{
    // Same scale as SpeechLog.HearingRadius/SpatialMemory.VisionRadius
    // — "nearby" means the same distance whether hearing, seeing, or
    // witnessing an action.
    public const float VisibilityRadius = 260f;

    // Noticing a theft takes actually watching closely, not just being
    // in the general area the way overhearing conversation or seeing
    // someone gather does — tighter than VisibilityRadius on purpose.
    public const float StealthWatchRadius = 150f;

    private const float RetainSeconds = 30f; // same reasoning as SpeechLog's own

    private static readonly RecentEventBuffer _buffer = new(RetainSeconds);

    public static void Announce(string actorName, Vector2 position, string description) =>
        _buffer.Record(actorName, position, description);

    // Rolls one Wisdom-vs-Dexterity check per nearby character (except
    // the thief and the victim, who each already have their own way of
    // knowing) and only feeds the ones who succeed — everyone else
    // genuinely never gets a chance to see this entry at all, not just
    // "chose not to look."
    public static void AnnounceStealthAttempt(string thiefName, string victimName, Vector2 position, string description, int thiefDexterityMod, IEnumerable<(string Name, Vector2 Position, int WisdomMod)> nearby)
    {
        var noticedBy = new List<string>();
        foreach ((string name, Vector2 pos, int wisdomMod) in nearby)
        {
            if (name == thiefName || name == victimName)
                continue;
            if (position.DistanceTo(pos) > StealthWatchRadius)
                continue;
            if (SkillCheck.Roll(wisdomMod, DifficultyClass.OpposedBase + thiefDexterityMod).Success)
                noticedBy.Add(name);
        }
        if (noticedBy.Count > 0)
            _buffer.Record(thiefName, position, description, noticedBy);
    }

    // Everything witnessable within VisibilityRadius of observerPosition
    // that this observer hasn't already caught up on — see
    // RecentEventBuffer.Consume() for the "at most once per observer"
    // mechanics.
    public static List<(string ActorName, string Description)> Witnessed(string observerName, Vector2 observerPosition) =>
        _buffer.Consume(observerName, observerPosition, VisibilityRadius);

    // See SpeechLog.Reset()'s own comment — same reasoning, same fix,
    // for the visible-actions side instead of the audible one.
    public static void Reset() => _buffer.Clear();
}
