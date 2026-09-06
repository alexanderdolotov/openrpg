using Godot;
using System.Collections.Generic;

// A dense stand of trees blocking the way — unlike AppleTree, not a
// resource (no picking, no tool-schema entry, no WorldRegistry id):
// purely an obstacle along the frontier between the village and misty
// mountains. One collision circle covers the whole patch rather than
// one per blob; the individual trees aren't separate gameplay objects
// here, just texture on a single obstacle.
public partial class ForestPatch : StaticBody2D, IHasVisualBounds, IObstacle
{
    [Export] public float PatchRadius = 85f;

    private static readonly Color[] BlobColors =
    {
        new(0.28f, 0.4f, 0.22f), new(0.32f, 0.45f, 0.26f), new(0.25f, 0.36f, 0.2f),
    };

    private static readonly Vector2[] BlobOffsets =
    {
        new(-40, -20), new(10, -35), new(45, 5), new(-10, 30), new(-45, 15), new(30, 40), new(0, -5),
    };

    public override void _Ready()
    {
        foreach ((Vector2 offset, float radius) in GetObstacleCircles())
            AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = radius }, Position = offset });
    }

    public override void _Draw()
    {
        for (int i = 0; i < BlobOffsets.Length; i++)
            DrawCircle(BlobOffsets[i], 26f, BlobColors[i % BlobColors.Length]);
    }

    public Rect2 GetLocalBounds() => new(-PatchRadius, -PatchRadius, PatchRadius * 2f, PatchRadius * 2f);

    public IEnumerable<(Vector2, float)> GetObstacleCircles()
    {
        yield return (Vector2.Zero, PatchRadius * 0.85f);
    }
}
