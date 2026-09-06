using Godot;
using System.Collections.Generic;

// Unlike the village's River (deliberately shallow — no collision, by
// design), this is "a non-shallow boundary": it actually blocks
// movement. Part of the obstacle course between the village and misty
// mountains — an NPC on "travel" never uses PathGrid (see NPCActor),
// so getting past this is purely reactive collision-sliding, the same
// as bumping into anything else unplanned.
//
// A straight band; several chained obstacle circles along its length
// approximate it for both PathGrid and real collision, rather than one
// giant circle that would block far more than the band itself.
public partial class RiverCrossing : StaticBody2D, IHasVisualBounds, IObstacle
{
    [Export] public float Length = 460f;
    [Export] public float Thickness = 60f;
    [Export] public int Segments = 5;

    private static readonly Color DeepWater = new(0.14f, 0.28f, 0.5f, 0.9f);

    public override void _Ready()
    {
        foreach ((Vector2 offset, float radius) in GetObstacleCircles())
            AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = radius }, Position = offset });
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(-Length / 2f, -Thickness / 2f, Length, Thickness), DeepWater);
    }

    public Rect2 GetLocalBounds() => new(-Length / 2f, -Thickness / 2f, Length, Thickness);

    public IEnumerable<(Vector2, float)> GetObstacleCircles()
    {
        float radius = Thickness / 2f;
        int segments = System.Math.Max(Segments, 1);
        for (int i = 0; i < segments; i++)
        {
            float t = segments == 1 ? 0.5f : (float)i / (segments - 1);
            float x = -Length / 2f + t * Length;
            yield return (new Vector2(x, 0f), radius);
        }
    }
}
