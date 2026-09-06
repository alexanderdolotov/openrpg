using Godot;
using System.Collections.Generic;
using System.Linq;

// A StaticBody2D now, not a plain Node2D — that's what makes it a real
// physical obstacle NPCs collide with instead of just something they
// stop near because of interaction Range. StaticBody2D is still a
// Node2D under the hood, so .Position, _Draw(), etc. all still work
// exactly as before.
public partial class Home : StaticBody2D, IInteractable, IHasVisualBounds, IObstacle
{
    public int ApplesStored = 0;
    public int FishStored = 0;

    private static readonly Vector2 WallSize = new(84, 84);
    private static readonly Color WallColor = new(0.55f, 0.35f, 0.2f);
    private static readonly Color RoofColor = new(0.42f, 0.2f, 0.15f);

    // Collision matches the wall footprint only — the roof's overhang
    // is purely visual, no reason for it to physically block anything.
    // Shared by _Ready() and _Draw() so the two can't drift apart.
    private Rect2 WallRect => new(-WallSize.X / 2f, -WallSize.Y / 2f + 18f, WallSize.X, WallSize.Y - 18f);

    public override void _Ready()
    {
        Rect2 wallRect = WallRect;
        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = wallRect.Size },
            Position = wallRect.Position + wallRect.Size / 2f,
        };
        AddChild(shape);
    }

    // Includes the roof, unlike collision — the auto-fit camera should
    // frame everything actually drawn, not just what's physically solid.
    public Rect2 GetLocalBounds()
    {
        Rect2 wallRect = WallRect;
        float halfWidth = WallSize.X / 2f + 12f;
        float top = wallRect.Position.Y - 36f;
        float bottom = wallRect.Position.Y + wallRect.Size.Y;
        return new Rect2(-halfWidth, top, halfWidth * 2f, bottom - top);
    }

    // Bounding-circle approximation of the wall footprint (not the
    // roof) for PathGrid — a diagonal-based radius over-blocks the
    // corners slightly, which is the safe direction to be wrong in.
    public System.Collections.Generic.IEnumerable<(Vector2, float)> GetObstacleCircles()
    {
        yield return (Vector2.Zero, WallRect.Size.Length() / 2f);
    }

    public override void _Draw()
    {
        Rect2 wallRect = WallRect;
        DrawRect(wallRect, WallColor);

        float halfWidth = WallSize.X / 2f + 12f; // slight overhang past the walls
        float roofTop = wallRect.Position.Y - 36f;
        float roofBase = wallRect.Position.Y;
        Vector2[] roofPoints =
        {
            new(0f, roofTop),
            new(-halfWidth, roofBase),
            new(halfWidth, roofBase),
        };
        DrawColoredPolygon(roofPoints, RoofColor);
    }

    // One generic "deposit" rather than deposit_apple/deposit_fish/...
    // per resource — it deposits the actor's ENTIRE inventory, whatever
    // mix of item types it happens to hold, so the tool schema doesn't
    // grow with every new resource type either.
    public InteractResult TryInteract(NPCActor actor, string actionId)
    {
        if (actionId != "deposit")
            return new InteractResult(false, "wrong_action");
        if (actor.Inventory.All.Count == 0)
            return new InteractResult(false, "nothing_to_deposit");

        // Snapshot first — deposit mutates the same dictionary Remove()
        // reads from, so iterating it live while removing would be
        // undefined.
        var deposited = actor.Inventory.Snapshot();
        foreach (KeyValuePair<string, int> kv in deposited)
        {
            if (kv.Key == "apple") ApplesStored += kv.Value;
            else if (kv.Key == "fish") FishStored += kv.Value;
            actor.Inventory.Remove(kv.Key, kv.Value);
        }

        return new InteractResult(true, "ok", new Godot.Collections.Dictionary
        {
            { "items", string.Join(", ", deposited.Select(kv => $"{kv.Value} {kv.Key}")) },
            { "apples_stored", ApplesStored },
            { "fish_stored", FishStored },
        });
    }
}
