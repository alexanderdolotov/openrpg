using System.Collections.Generic;
using Godot;

// The shared, world-level state every NpcAgent reads from but none of
// them own — as opposed to Personality/Memory/Mind, which are strictly
// per-NPC. Bundled into one object so adding the next resource type
// doesn't mean widening NpcFactory/NpcAgent's constructor again.
public class WorldContext
{
    public List<AppleTree> Trees;
    public List<FishingSpot> FishingSpots;
    public Home Home;

    // Distant, non-resource landmarks an NPC can only reach by walking
    // straight toward them (no interaction, no gathering) — keyed by
    // WorldRegistry id, e.g. {"misty_mountains": <node>}. Separate from
    // Trees/FishingSpots since these are the "known vs heuristic-only"
    // test case, not part of the resource loop.
    public Dictionary<string, Node2D> Flagpoles = new();

    // The SAME list Main.cs appends to as each NPC (and the player) is
    // created — every NpcAgent sees the full, current roster by the
    // time it actually takes a turn, even though this reference was
    // handed out before everyone existed. This is what "who's around
    // me" and speech hearing-range checks read from. Typed to the
    // shared IWorldCharacter interface, not NpcAgent specifically, so
    // an LLM-driven NPC and the input-driven PlayerCharacter are
    // indistinguishable from here.
    public List<IWorldCharacter> Agents = new();
}
