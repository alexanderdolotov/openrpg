using Godot;
using System.Collections.Generic;

// A standing body of deep water — a premade pond sprite (see
// assets/CREDITS.md for exactly where it came from), not hand-drawn
// shapes. Two earlier attempts at this both drew the water procedurally
// instead: the first stacked several semi-transparent circles (read as
// a cluster of translucent Venn-diagram blobs, not water); the second
// was one continuous outline polygon at 12 points around the
// circumference, filled opaque with a lighter shore band underneath
// ("just circles on top of more circles" fixed, but with only 12
// points the shoreline itself read as faceted/hexagonal rather than an
// actual pond — "water doesn't look like hexagons"). Both were code
// trying to invent art; this is real art instead. Still "a non-shallow
// boundary" like the old RiverCrossing before either attempt — it
// actually blocks movement, unlike the shallow, walkable village River
// — and still the same "always behind, never Y-sorted" terrain
// treatment as every other background piece, for the same reason: a
// character bounces off it rather than standing on it, so getting its
// Y-sort right relative to a character matters much less than just not
// letting it paint over someone walking near it.
public partial class Lake : StaticBody2D, IHasVisualBounds, IObstacle
{
    [Export] public float Radius = 110f;

    // The pond sprite's own half-width in its native pixels (it's a
    // 48×48 crop — three 16px tiles stitched edge-to-edge, no gaps) —
    // Scale is derived from Radius/this in _Ready() so every existing
    // caller's Radius still means the same "how big" it always did, the
    // same as TrunkRadius-driven scaling elsewhere.
    private const float NativeHalfSize = 24f;

    private static Texture2D _texture;
    private static Texture2D Texture => _texture ??= GD.Load<Texture2D>("res://assets/world/pond.png");

    public override void _Ready()
    {
        foreach ((Vector2 offset, float radius) in GetObstacleCircles())
            AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = radius }, Position = offset });

        float scale = Radius / NativeHalfSize;
        AddChild(new Sprite2D
        {
            Texture = Texture,
            Scale = new Vector2(scale, scale),
            TextureFilter = TextureFilterEnum.Nearest, // keep pixel art crisp, same as every other cropped-sheet sprite
        });
    }

    // Exactly the sprite's own rendered footprint — no padding guess
    // needed now that this is a real image with known proportions,
    // unlike the old polygon version which had to pad past Radius to
    // cover however far its own shore band actually extended.
    public Rect2 GetLocalBounds() => new(-Radius, -Radius, Radius * 2f, Radius * 2f);

    public IEnumerable<(Vector2, float)> GetObstacleCircles()
    {
        // One circle, slightly smaller than the visual body — same
        // "collision reads a bit smaller than the art" restraint
        // ForestPatch's own 0.85 factor uses.
        yield return (Vector2.Zero, Radius * 0.9f);
    }
}
