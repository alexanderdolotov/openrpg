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

    // Both GatherableFoliage — pine yields "pinecone", the berry bushes
    // yield "blueberry"/"blackberry"/"raspberry" (same class, different
    // [Export] config — see GatherableFoliage's own header). Two
    // separate lists, not one, because each is its own action's
    // target-id enum (gather_pinecone vs gather_berry — see Mind.cs) —
    // scoping "which targets are valid for THIS action" is the whole
    // reason TreeIds()/FishingSpotIds() are separate lists too.
    public List<GatherableFoliage> PineTrees;
    public List<GatherableFoliage> BerryBushes;
    public List<Stick> Sticks;

    public Home Home;
    public FirePit FirePit;

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

    // Every living wild animal — Main appends/removes as they spawn and
    // die (see Main.SpawnAnimal/OnAnimalDied). Shared so a Wolf can find
    // nearby Rabbits, a Rabbit can find nearby Wolves to flee, etc.,
    // without each species needing its own separate registry.
    public List<Animal> Animals = new();

    // Bumped by Main whenever exploration-driven generation (see
    // WorldExploration) adds a new tree or fishing spot after boot.
    // NpcAgent's cached TreeIds()/FishingSpotIds() arrays are keyed off
    // this — a cache built before the world grew would otherwise never
    // see the new resource for the rest of the run.
    public int ContentVersion = 0;

    // NpcAgent and PlayerCharacter both end up in Agents but don't
    // share a common base with a Stats property — NpcAgent holds its
    // NPCActor by reference (see NpcAgent.Initialize()'s own comment)
    // while PlayerCharacter directly extends NPCActor. Resolved here,
    // once, rather than reimplemented by every caller that needs a
    // character's actual stats (the persuasion-hint roll, the
    // steal-noticing roll) — lives on WorldContext since it already
    // owns Agents, the thing this is actually about interpreting.
    public static NPCActor ActorOf(IWorldCharacter character) => character switch
    {
        NpcAgent npc => npc.Actor,
        NPCActor actor => actor, // covers PlayerCharacter
        _ => null,
    };

    // The reverse of ActorOf() — given an NPCActor (say, whatever an
    // Animal's own CurrentTarget currently is), find the display name
    // of whichever Agents entry it belongs to. Used for perception
    // lines that need to name a THIRD party by name — "a wolf is
    // attacking Wren right now" — not just describe this NPC's own
    // situation. Null if the actor isn't a currently-registered
    // character at all (shouldn't normally happen, but a target
    // reference can outlive a death/despawn by a frame or two).
    public string NameOf(NPCActor actor)
    {
        foreach (IWorldCharacter c in Agents)
            if (ActorOf(c) == actor)
                return c.DisplayName;
        return null;
    }

    // Every character currently in the world, with the Wisdom modifier
    // WorldEventLog.AnnounceStealthAttempt() rolls against — it filters
    // by distance/who's-the-thief/who's-the-victim itself, so this just
    // hands over everyone rather than pre-filtering here too. Shared by
    // NpcAgent and PlayerCharacter's own steal handling, same reasoning
    // as ActorOf() above.
    public IEnumerable<(string Name, Vector2 Position, int WisdomMod)> CharactersWithStats()
    {
        foreach (IWorldCharacter c in Agents)
        {
            NPCActor actor = ActorOf(c);
            if (actor != null)
                yield return (c.DisplayName, c.GlobalPosition, actor.Stats.WisdomMod);
        }
    }
}
