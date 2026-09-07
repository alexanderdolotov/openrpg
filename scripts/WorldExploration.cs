using Godot;
using System.Collections.Generic;

// The shared, world-level record of which regions of the map have
// already been explored — distinct from each NPC's own SpatialMemory,
// which tracks landmarks THAT CHARACTER personally recognizes (used for
// "known vs heuristic-only" travel). This is what drives procedural
// generation instead: the first time ANY character — NPC or player —
// gets into a region nobody's been near before, Main.cs generates a
// little more world there. Static/global on purpose, same decoupled
// pattern as WorldRegistry/PathGrid/SpeechLog — one shared map, not one
// copy per character.
public static class WorldExploration
{
    public const float RegionSize = 400f; // world-units per region cell

    // A hard cap on how far generation ever reaches — well beyond the
    // hand-placed village + frontier (roughly x:-400..1600, y:-600..900,
    // see Main.GroundArea), but still finite. A region outside this
    // never generates anything, no matter how long a character
    // wanders — "max map size" is enforced here, in exactly one place.
    public static readonly Rect2 MaxMapBounds = new(-2000, -2500, 5500, 5000);

    private static readonly HashSet<(int, int)> _exploredRegions = new();

    private static (int, int) RegionOf(Vector2 position) =>
        (Mathf.FloorToInt(position.X / RegionSize), Mathf.FloorToInt(position.Y / RegionSize));

    // Called once at boot with the hand-placed village's own bounds —
    // without this, the moment anyone takes a single step, the region
    // they're already standing in would read as "newly discovered" and
    // generation would immediately start layering random trees on top
    // of the curated starting village.
    public static void MarkExplored(Rect2 area)
    {
        (int X, int Y) min = RegionOf(area.Position);
        (int X, int Y) max = RegionOf(area.Position + area.Size);
        for (int x = min.X; x <= max.X; x++)
            for (int y = min.Y; y <= max.Y; y++)
                _exploredRegions.Add((x, y));
    }

    // Call with a character's current position and a lookahead radius,
    // as often as fits (Main does this on a timer, not every physics
    // frame — region-scale exploration doesn't need per-frame
    // resolution). Returns the world-space CENTER of every region
    // within radius that nobody's been near before — everything now
    // marked explored in this same call, so each one is only ever
    // returned once, ever.
    //
    // Deliberately a RADIUS around the character, not just the single
    // region they're standing in: generating a tree the instant someone
    // steps into its region would generate it well inside the camera's
    // own view (it's already visible by the time the character's
    // position updates), which reads as content popping into existence
    // on screen. Radius needs to stay bigger than however far the
    // camera can actually see from the character (see Main.LookaheadRadius
    // for the real number and how it's derived) so whatever gets
    // generated is always safely offscreen, ahead of where anyone could
    // actually be looking, by the time it appears.
    public static IEnumerable<Vector2> DiscoverAhead(Vector2 position, float radius)
    {
        (int X, int Y) min = RegionOf(position - new Vector2(radius, radius));
        (int X, int Y) max = RegionOf(position + new Vector2(radius, radius));

        for (int x = min.X; x <= max.X; x++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                Vector2 center = new((x + 0.5f) * RegionSize, (y + 0.5f) * RegionSize);
                if (position.DistanceTo(center) > radius)
                    continue; // square scan above, circular radius here — skip the corners
                if (!MaxMapBounds.HasPoint(center))
                    continue; // never marked explored, so the boundary itself stays revisitable
                if (_exploredRegions.Add((x, y)))
                    yield return center;
            }
        }
    }
}
