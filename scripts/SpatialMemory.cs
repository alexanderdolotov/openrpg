using System.Collections.Generic;
using Godot;

// A per-NPC "geo map": which landmarks this NPC has personally been
// close enough to (VisionRadius) to actually know, versus ones it's
// only aware of as a distant, unexplored flag — like misty mountains
// it can see the outline of but has never actually reached.
//
// This is what "known" vs "heuristic-only" means for a flagpole
// destination in particular (see NPCActor.AssignAction and the
// perception line built in NpcAgent.BuildPerception): a flagpole not yet
// in this set gets walked toward directly (Euclidean heuristic, no real
// path); anything else — a tree, a fishing spot, home, or a flagpole
// already reached once — routes through PathGrid's A* instead, since
// its position is genuinely known rather than "seen from a distance."
// This set itself only ever grows (once known, always known) and isn't
// PathGrid's input; PathGrid is built once from IObstacle, independent
// of what any one NPC personally happens to have discovered.
public class SpatialMemory
{
    public const float VisionRadius = 260f;

    private readonly HashSet<string> _known = new();

    public bool Knows(string landmarkId) => _known.Contains(landmarkId);
    public IReadOnlyCollection<string> KnownIds => _known;

    // Called once per turn with the NPC's position and every landmark
    // in the world (resources and flagpoles alike) — anything within
    // VisionRadius becomes known from here on, whether or not the NPC
    // ever actually interacts with it. Returns ids newly discovered
    // this call, so the caller can log/remember the moment.
    public List<string> Observe(Vector2 npcPosition, IEnumerable<(string Id, Vector2 Position)> landmarks)
    {
        var newlyDiscovered = new List<string>();
        foreach ((string id, Vector2 position) in landmarks)
        {
            if (_known.Contains(id))
                continue;
            if (npcPosition.DistanceTo(position) <= VisionRadius)
            {
                _known.Add(id);
                newlyDiscovered.Add(id);
            }
        }
        return newlyDiscovered;
    }
}
