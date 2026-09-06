using Godot;

// A StaticBody2D, but with a collision footprint much smaller than the
// drawn canopy — only a small trunk at the base blocks movement, per
// the design call that NPCs walking "under" a tree's canopy is fine for
// now. The canopy circle itself (Radius) is purely visual.
public partial class AppleTree : StaticBody2D, IInteractable, IHasVisualBounds, IObstacle
{
    [Export] public int AppleCount = 3;

    private const float GatherExertion = 3f; // fatigue points spent per attempt, on top of passive decay
    private const float Radius = 36f;      // visual canopy — not collidable
    public const float TrunkRadius = 10f;  // small collidable trunk at the base

    // Scale for the 16×16 source tile — 4.5x lands the sprite at 72px
    // (Radius * 2), the same footprint the old drawn circle used, so
    // this doesn't also quietly resize the tree relative to its
    // collision or the camera's auto-fit bounds.
    private const float SpriteScale = (Radius * 2f) / 16f;

    private static Texture2D _fruitedTexture;
    private static Texture2D _bareTexture;
    private Sprite2D _sprite;

    public override void _Ready()
    {
        var shape = new CollisionShape2D { Shape = new CircleShape2D { Radius = TrunkRadius } };
        AddChild(shape);

        _fruitedTexture ??= GD.Load<Texture2D>("res://assets/world/apple_tree.png");
        _bareTexture ??= GD.Load<Texture2D>("res://assets/world/apple_tree_bare.png");

        _sprite = new Sprite2D
        {
            Texture = AppleCount > 0 ? _fruitedTexture : _bareTexture,
            Scale = new Vector2(SpriteScale, SpriteScale),
            TextureFilter = TextureFilterEnum.Nearest,
        };
        AddChild(_sprite);
    }

    public Rect2 GetLocalBounds() => new(-Radius, -Radius, Radius * 2f, Radius * 2f);

    public System.Collections.Generic.IEnumerable<(Vector2, float)> GetObstacleCircles()
    {
        yield return (Vector2.Zero, TrunkRadius);
    }

    public InteractResult TryInteract(NPCActor actor, string actionId)
    {
        if (actionId != "pick_apple")
            return new InteractResult(false, "wrong_action");
        if (AppleCount <= 0)
            return new InteractResult(false, "depleted");

        // A low bar (DifficultyClass.Gather) — picking an apple barely
        // takes any coordination — but still a real check, not a
        // guarantee: a very low-Dexterity character can fumble it.
        var check = SkillCheck.Roll(actor.Stats.DexterityMod, DifficultyClass.Gather);
        var data = check.ToData("dexterity");
        actor.Vitals.Exert(GatherExertion); // real effort whether it lands or not
        if (!check.Success)
            return new InteractResult(false, "fumbled", data);

        AppleCount--;
        actor.Inventory.Add("apple");
        if (AppleCount <= 0)
            _sprite.Texture = _bareTexture;
        data["apples_left"] = AppleCount;
        return new InteractResult(true, "ok", data);
    }
}
