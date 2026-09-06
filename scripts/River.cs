using Godot;
using System.Collections.Generic;

// A meandering ribbon, not a straight rectangle — a sine-wave
// centerline with constant-width banks computed perpendicular to the
// curve's tangent at each sample point, so it actually reads as winding
// rather than a wobbly bar. Purely visual, no interaction — only the
// FishingSpot nodes placed along it are targets.
public partial class River : Node2D, IHasVisualBounds
{
    [Export] public float Length = 1050f;
    [Export] public float Thickness = 85f;
    [Export] public float Amplitude = 45f; // how far the river wanders above/below its centerline
    [Export] public float Waves = 2f;       // full sine cycles across Length
    [Export] public int Segments = 48;      // curve smoothness

    // World-space size of one repeated water tile — the source texture
    // is a 16×16 tile, but sampled at that literal scale it'd repeat
    // ~65 times across Length and look far finer-grained than every
    // other sprite in the world (trees/characters both read at a
    // noticeably chunkier pixel scale). This is the same "keep every
    // sprite feeling like the same size of pixel" concern, just solved
    // through UV scale here instead of a Sprite2D's Scale property,
    // since DrawColoredPolygon has no separate scale of its own.
    private const float WaterTileWorldSize = 32f;

    private static Texture2D _waterTexture;

    public override void _Ready()
    {
        _waterTexture ??= GD.Load<Texture2D>("res://assets/world/water.png");
        // Lets DrawColoredPolygon's UVs go outside [0,1] and wrap
        // instead of clamp — that's what makes the tile actually repeat
        // along the ribbon rather than stretching one copy across it.
        TextureFilter = TextureFilterEnum.Nearest;
        TextureRepeat = TextureRepeatEnum.Enabled;
    }

    public override void _Draw()
    {
        (Vector2[] points, Vector2[] uvs) = BuildRibbon();
        DrawColoredPolygon(points, Colors.White, uvs, _waterTexture);
    }

    // Used by Main.cs to fit the camera to the whole world without
    // hardcoding this river's shape anywhere else — includes how far
    // the waves actually swing, not just Thickness.
    public Rect2 GetLocalBounds()
    {
        float maxY = Amplitude + Thickness / 2f;
        return new Rect2(-Length / 2f, -maxY, Length, maxY * 2f);
    }

    // World-space y of the centerline at a given world-space x — lets
    // callers (fishing spot placement) sit precisely on the actual
    // curve instead of guessing a fixed y that would land off the water
    // at some points along a sine wave.
    public float GetCenterlineWorldY(float worldX)
    {
        float localX = worldX - Position.X + Length / 2f;
        float k = Mathf.Tau * Waves / Length;
        return Position.Y + Amplitude * Mathf.Sin(k * localX);
    }

    // Centerline: y = Amplitude * sin(k * x). Banks are offset from
    // that centerline along its local perpendicular (not just a fixed
    // vertical offset), so the ribbon keeps a consistent width even
    // where the curve is steep — walk the curve left to right building
    // the upper bank, then right to left for the lower bank, so the two
    // form one closed, non-self-intersecting polygon. UVs are just each
    // vertex's own local position divided by the tile size — not
    // curve-aligned (a UV unwrap that keeps the water's flow direction
    // consistent along a curving ribbon is a fair bit more involved),
    // but the source tile has no strong directionality to it, so a
    // plain planar mapping already reads fine as flowing water.
    private (Vector2[] Points, Vector2[] Uvs) BuildRibbon()
    {
        var upper = new List<Vector2>(Segments + 1);
        var lower = new List<Vector2>(Segments + 1);
        float halfThickness = Thickness / 2f;
        float k = Mathf.Tau * Waves / Length;

        for (int i = 0; i <= Segments; i++)
        {
            float t = (float)i / Segments;
            float localX = t * Length; // 0..Length, phase-independent of the -Length/2 recenter below
            float x = -Length / 2f + localX;
            float y = Amplitude * Mathf.Sin(k * localX);
            float slope = Amplitude * k * Mathf.Cos(k * localX);

            Vector2 tangent = new Vector2(1f, slope).Normalized();
            Vector2 normal = new Vector2(-tangent.Y, tangent.X);
            Vector2 center = new(x, y);

            upper.Add(center + normal * halfThickness);
            lower.Add(center - normal * halfThickness);
        }

        lower.Reverse();
        upper.AddRange(lower);
        Vector2[] points = upper.ToArray();

        var uvs = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
            uvs[i] = points[i] / WaterTileWorldSize;

        return (points, uvs);
    }
}
