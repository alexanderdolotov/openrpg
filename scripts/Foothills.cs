using Godot;
using System.Collections.Generic;

// Smaller, closer peaks between the village and misty mountains —
// unlike MistyMountains itself (deliberately non-collidable, a distant
// backdrop), these actually block the way. Same layered-triangle
// technique as MistyMountains, but solid.
public partial class Foothills : StaticBody2D, IHasVisualBounds, IObstacle
{
    [Export] public float Radius = 70f;

    private static readonly Color Slope = new(0.42f, 0.46f, 0.4f);
    private static readonly Color Rock = new(0.5f, 0.48f, 0.44f);

    public override void _Ready()
    {
        foreach ((Vector2 offset, float radius) in GetObstacleCircles())
            AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = radius }, Position = offset });
    }

    public override void _Draw()
    {
        Peak(new Vector2(-50f, 30f), 140f, 90f, Slope);
        Peak(new Vector2(40f, 30f), 120f, 75f, Rock);
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

    public Rect2 GetLocalBounds() => new(-110f, -90f, 220f, 150f);

    public IEnumerable<(Vector2, float)> GetObstacleCircles()
    {
        yield return (Vector2.Zero, Radius);
    }
}
