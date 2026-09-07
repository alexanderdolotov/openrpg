using Godot;
using System.Collections.Generic;

// "A fire pit near the house that can be lit up for 5min at a time...
// once lit, if stick in inventory, can make a torch." One fixed world
// object — like Home, registered once under a single id ("firepit"),
// not a per-instance WorldId list the way sticks/animals need, since
// there's only ever one.
public partial class FirePit : StaticBody2D, IInteractable, IHasVisualBounds, IObstacle
{
    private const float Radius = 20f; // small — walkable-around, not a real detour
    public const float LitDuration = 300f; // 5 minutes

    public bool IsLit { get; private set; }
    private float _litTimer;

    private static readonly Color Stone = new(0.5f, 0.5f, 0.48f);
    private static readonly Color Ember = new(0.3f, 0.15f, 0.08f);
    private static readonly Color FlameOuter = new(0.95f, 0.5f, 0.15f);
    private static readonly Color FlameInner = new(1f, 0.85f, 0.3f);

    private PointLight2D _fireLight;

    public override void _Ready()
    {
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = Radius } });

        // Same shared radial gradient PlayerCharacter's own visibility
        // light uses (see RadialLightTexture's own header) — warm
        // orange here via Color, not a separately-built texture.
        _fireLight = new PointLight2D
        {
            Name = "FireLight",
            Texture = RadialLightTexture.Get(),
            TextureScale = 440f / RadialLightTexture.Size, // ~220px radius
            Color = new Color(1f, 0.65f, 0.35f),
            Energy = 1.1f,
            Visible = false,
        };
        AddChild(_fireLight);
    }

    public IEnumerable<(Vector2 LocalOffset, float Radius)> GetObstacleCircles()
    {
        yield return (Vector2.Zero, Radius);
    }

    // A bit taller than the stone ring alone — leaves room for the
    // flame shape above it when lit, so the auto-fit camera frames the
    // whole thing, not just the base.
    public Rect2 GetLocalBounds() => new(-Radius - 6f, -Radius - 34f, (Radius + 6f) * 2f, Radius + 40f);

    public override void _Draw()
    {
        for (int i = 0; i < 8; i++)
        {
            float angle = i / 8f * Mathf.Tau;
            DrawCircle(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Radius, 5f, Stone);
        }
        DrawCircle(Vector2.Zero, Radius - 6f, Ember);

        if (IsLit)
        {
            Vector2[] outer = { new(0f, -28f), new(10f, -8f), new(0f, 4f), new(-10f, -8f) };
            DrawColoredPolygon(outer, FlameOuter);
            Vector2[] inner = { new(0f, -18f), new(5f, -6f), new(0f, 2f), new(-5f, -6f) };
            DrawColoredPolygon(inner, FlameInner);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsLit) return;
        _litTimer -= (float)delta;
        if (_litTimer <= 0f)
        {
            IsLit = false;
            _fireLight.Visible = false;
            QueueRedraw();
        }
    }

    public InteractResult TryInteract(NPCActor actor, string actionId)
    {
        switch (actionId)
        {
            case "light_fire":
                if (IsLit)
                    return new InteractResult(false, "already_lit");
                IsLit = true;
                _litTimer = LitDuration;
                _fireLight.Visible = true;
                QueueRedraw();
                return new InteractResult(true, "ok");

            // "Once lit, if stick in inventory, can make a torch" —
            // lights the stick from the fire, consuming it, and starts
            // (or refreshes — see NPCActor.RefreshTorch's own comment)
            // this actor's own torch-burn timer.
            case "make_torch":
                if (!IsLit)
                    return new InteractResult(false, "fire_not_lit");
                if (!actor.Inventory.Remove("stick", 1))
                    return new InteractResult(false, "no_stick");
                actor.Inventory.Add("torch", 1);
                actor.RefreshTorch();
                return new InteractResult(true, "ok");

            // Raw rabbit_meat isn't food at all (Food.IsFood says no) —
            // this is the one thing that turns it into cooked_meat,
            // which is (see Food.cs's own header for why it's worth
            // the trouble: the most filling food in the game). Eating
            // the result afterward is a normal "eat" action, anytime,
            // anywhere — only the COOKING itself needs the fire lit.
            case "cook_meat":
                if (!IsLit)
                    return new InteractResult(false, "fire_not_lit");
                if (!actor.Inventory.Remove("rabbit_meat", 1))
                    return new InteractResult(false, "no_meat");
                actor.Inventory.Add("cooked_meat", 1);
                return new InteractResult(true, "ok");

            default:
                return new InteractResult(false, "wrong_action");
        }
    }
}
