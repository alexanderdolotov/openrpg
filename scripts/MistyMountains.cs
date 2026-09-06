using Godot;

// A distant, unexplored landmark — the test case for "known" vs
// "heuristic-only" travel. Deliberately NOT IHasVisualBounds, so it's
// excluded from Main's camera auto-fit: something meant to read as
// "far away and barely glimpsed" shouldn't be what dictates how
// zoomed-in the main village gets to be. It sits near the edge of the
// normal camera frame on purpose — being only partially visible suits
// "misty and distant" rather than fighting it.
//
// Drawn as layered translucent triangles (guaranteed-simple polygons,
// each one trivially valid) rather than a jagged hand-authored
// ridgeline — atmospheric perspective (paler/more transparent further
// back) does the rest of the work without risking a self-intersecting
// polygon I can't render to check.
public partial class MistyMountains : Node2D
{
    private static readonly Color Far = new(0.58f, 0.63f, 0.7f, 0.4f);
    private static readonly Color Mid = new(0.46f, 0.53f, 0.63f, 0.55f);
    private static readonly Color Near = new(0.36f, 0.43f, 0.53f, 0.75f);

    public override void _Draw()
    {
        const float baseY = 40f; // shared "ground" so separate peaks read as one range

        Peak(new Vector2(-350f, baseY), 340f, 130f, Far);
        Peak(new Vector2(-40f, baseY), 380f, 150f, Far);
        Peak(new Vector2(300f, baseY), 320f, 120f, Far);

        Peak(new Vector2(-220f, baseY), 260f, 100f, Mid);
        Peak(new Vector2(90f, baseY), 280f, 110f, Mid);

        Peak(new Vector2(-80f, baseY), 220f, 75f, Near);
        Peak(new Vector2(180f, baseY), 200f, 65f, Near);
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
}
