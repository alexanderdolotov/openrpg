using Godot;

// A basic weapon lying on the ground — pick it up once and it's gone
// (unlike AppleTree/GatherableFoliage, there's no renewable count here,
// just one stick per node). See Weapons.cs for what carrying one
// actually does to an attack's damage.
public partial class Stick : Node2D, IInteractable, IHasVisualBounds
{
    // Set by Main right after WorldRegistry.Register() — sticks get
    // picked up out of order, so a cached id list can't safely
    // reconstruct "stick_{list index}" once one's gone from the middle
    // (same fix Animal.WorldId already needed, for the same reason).
    public string WorldId;

    private const float Length = 26f;
    private const float Thickness = 4f;
    private static readonly Color Wood = new(0.55f, 0.38f, 0.22f);

    public override void _Draw()
    {
        // A short diagonal stick, roughly the same visual weight as
        // the world's other small pickups (FishingSpot's own footprint,
        // say) rather than anything imposing.
        Vector2 dir = new Vector2(1f, -0.4f).Normalized();
        Vector2 perp = new Vector2(-dir.Y, dir.X) * (Thickness / 2f);
        Vector2 a = -dir * (Length / 2f);
        Vector2 b = dir * (Length / 2f);
        Vector2[] points = { a + perp, b + perp, b - perp, a - perp };
        DrawColoredPolygon(points, Wood);
    }

    public Rect2 GetLocalBounds() => new(-Length / 2f, -Length / 2f, Length, Length);

    public InteractResult TryInteract(NPCActor actor, string actionId)
    {
        if (actionId != "pick_up_stick")
            return new InteractResult(false, "wrong_action");
        actor.Inventory.Add("stick");
        QueueFree();
        return new InteractResult(true, "ok");
    }
}
