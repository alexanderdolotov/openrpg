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
    // 1000, not 260 — raised 2026-09-09 after a real prompt_debug session
    // showed pick_apple/catch_fish never once offered across an entire
    // play session (grepped every TOOLS OFFERED line — zero hits for
    // either), and the player's own direct "let's catch some fish"/
    // "let's collect apples" requests getting answered with an unrelated
    // substitute tool instead of an honest decline, because neither was
    // EVER actually in the menu, not because the model chose wrong. Root
    // cause: 260 was tuned for a much smaller original village layout:
    // the hand-placed apple trees sit 460-650px from home, the five
    // river fishing spots span roughly 350-1450px from home, and
    // misty_mountains — a landmark this same session's own logs treat as
    // routinely relevant, not "far away" — showed up at 759-870px away.
    // All of that used to be invisible from anywhere near home. 1000
    // comfortably covers the trees and misty_mountains, and reaches
    // several of the five fishing spots (deliberately not all — the
    // farthest are genuinely far in this layout, and "everything is
    // always in range" would undo the whole reason this radius exists:
    // see NearestResourceLines' own header). This is a real, coarse
    // value for this specific early-game world, not a principled
    // constant — revisit again once exploration-driven growth
    // (WorldExploration) makes the playable area meaningfully bigger
    // than this village-and-mountains core, the same way 260 stopped
    // fitting this one.
    public const float VisionRadius = 1000f;

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
