using Godot;

// A small drawn fish silhouette — there's no fish tile anywhere in the
// Kenney Roguelike/RPG Pack the rest of this project's world art comes
// from (checked the full sheet directly; it's an environment/furniture
// pack, no creatures), so this is code-drawn instead, same as
// MistyMountains/Foothills/ForestPatch already are wherever no clean
// sprite crop exists — consistent with the rest of the codebase rather
// than pulling in a mismatched sprite from a different pack.
//
// A child FishingSpot adds AFTER its water Sprite2D (see FishingSpot's
// _Ready()) — plain sibling order draws a later child on top, the same
// mechanism used everywhere else in this codebase for "in front of"
// when real Y-sort isn't the right tool (flat terrain, not something
// with height). Visibility is toggled by FishingSpot itself as
// FishCount changes, not tracked here.
public partial class FishIcon : Node2D
{
    private static readonly Color Body = new(0.62f, 0.72f, 0.78f);
    private static readonly Color Belly = new(0.85f, 0.9f, 0.9f);
    private static readonly Color Fin = new(0.45f, 0.55f, 0.62f);
    private static readonly Color Eye = new(0.1f, 0.1f, 0.12f);

    public override void _Draw()
    {
        // A simple lens-shaped body (two arcs approximated as a
        // polygon) facing right, a triangular tail fin trailing left —
        // small and simple enough to read clearly at the 48px the
        // water tile itself renders at.
        Vector2[] body =
        {
            new(-7f, 0f), new(-5f, -3f), new(0f, -4f), new(5f, -2f),
            new(7f, 0f), new(5f, 2f), new(0f, 4f), new(-5f, 3f),
        };
        DrawColoredPolygon(body, Body);

        Vector2[] belly = { new(-4f, 1f), new(0f, 3f), new(4f, 1f), new(0f, 0f) };
        DrawColoredPolygon(belly, Belly);

        Vector2[] tail = { new(-7f, 0f), new(-12f, -4f), new(-12f, 4f) };
        DrawColoredPolygon(tail, Fin);

        DrawCircle(new Vector2(4f, -1f), 1f, Eye);
    }
}
