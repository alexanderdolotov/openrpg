using Godot;
using System.Collections.Generic;

// A distant, unexplored landmark — the test case for "known" vs
// "heuristic-only" travel. Deliberately NOT IHasVisualBounds, so it's
// excluded from Main's camera auto-fit: something meant to read as
// "far away and barely glimpsed" shouldn't be what dictates how
// zoomed-in the main village gets to be. It sits near the edge of the
// normal camera frame on purpose — being only partially visible suits
// "misty and distant" rather than fighting it.
//
// DOES have real collision now (StaticBody2D + IObstacle, same as
// Foothills) — this used to be deliberately walk-through, reasoned as
// "distant and barely glimpsed, not something you'd ever actually
// reach." In practice that read as a bug, not atmosphere: a big,
// clearly-drawn mountain range you can just walk straight into. Real
// collision doesn't cost the "known vs heuristic-only" travel framing
// anything — "travel" already never uses PathGrid (see NPCActor),
// collision or not — it just means you can approach it and hit a real
// base instead of phasing through the middle of it.
//
// Drawn as layered translucent triangles (guaranteed-simple polygons,
// each one trivially valid) rather than a jagged hand-authored
// ridgeline — atmospheric perspective (paler/more transparent further
// back) does the rest of the work without risking a self-intersecting
// polygon I can't render to check.
public partial class MistyMountains : StaticBody2D, IObstacle
{
    private static readonly Color Far = new(0.58f, 0.63f, 0.7f, 0.4f);
    private static readonly Color Mid = new(0.46f, 0.53f, 0.63f, 0.55f);
    private static readonly Color Near = new(0.36f, 0.43f, 0.53f, 0.75f);

    private const float BaseY = 40f; // shared "ground" so separate peaks read as one range

    // Collision only on the far flanks, leaving a clear central "pass"
    // straight up to this node's own Position — NOT a full ring or
    // chain across the whole width. Two earlier attempts both put solid
    // ground within ActionRanges.Travel (120) of this node's own
    // Position (once dead center at BaseY, once merely offset south at
    // CollisionY=200 with smaller radii) — verified BOTH wrong the same
    // way: not a single point-in-circle check, but actually driving a
    // character's real Velocity/MoveAndSlide() loop toward this node
    // from due south for a full simulated 10 seconds (matching
    // ProcessNavigating exactly) and checking where it actually ends
    // up. Both times it got stuck sliding along a circle far short of
    // 120 — "travel" would time out as unreachable from the one
    // direction that actually matters (the village sits south of the
    // frontier). Each flank circle here sits far enough out in X
    // (|x| - radius comfortably over 100) that it can never reach back
    // to x≈0 at ANY y, so the straight-south approach — reverified the
    // same real-movement way — is provably always clear, while the far
    // left/right shoulders of the range stay genuinely solid if you
    // wander into them instead.
    private static readonly (float X, float Radius)[] Flanks =
    {
        (-350f, 120f), (-220f, 110f), // left shoulder
        (180f, 90f), (300f, 110f), // right shoulder
    };

    public override void _Ready()
    {
        foreach ((Vector2 offset, float radius) in GetObstacleCircles())
            AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = radius }, Position = offset });
    }

    public override void _Draw()
    {
        Peak(new Vector2(-350f, BaseY), 340f, 130f, Far);
        Peak(new Vector2(-40f, BaseY), 380f, 150f, Far);
        Peak(new Vector2(300f, BaseY), 320f, 120f, Far);

        Peak(new Vector2(-220f, BaseY), 260f, 100f, Mid);
        Peak(new Vector2(90f, BaseY), 280f, 110f, Mid);

        Peak(new Vector2(-80f, BaseY), 220f, 75f, Near);
        Peak(new Vector2(180f, BaseY), 200f, 65f, Near);
    }

    private void Peak(Vector2 baseCenter, float width, float height, Color color)
    {
        Vector2[] triangle =
        {
            new(baseCenter.X - width / 2f, baseCenter.Y),
            new(baseCenter.X, baseCenter.Y - height),
            new(baseCenter.X + width / 2f, baseCenter.Y),
        };
        DrawColoredPolygon(triangle, color);
    }

    public IEnumerable<(Vector2, float)> GetObstacleCircles()
    {
        // Slightly south of the visual BaseY (which peak triangles
        // still draw at) — not load-bearing for the clear-corridor
        // property above (that only depends on X vs radius), just
        // keeps the collision roughly under where the peaks actually
        // render.
        const float y = BaseY + 80f;
        foreach ((float x, float radius) in Flanks)
            yield return (new Vector2(x, y), radius);
    }
}
