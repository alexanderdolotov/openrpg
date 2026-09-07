using Godot;

// A StaticBody2D, but with a collision footprint much smaller than the
// drawn canopy — only a small trunk at the base blocks movement, per
// the design call that NPCs walking "under" a tree's canopy is fine for
// now. The canopy itself is purely visual.
public partial class AppleTree : StaticBody2D, IInteractable, IHasVisualBounds, IObstacle
{
    [Export] public int AppleCount = 3;

    private const float GatherExertion = 3f; // fatigue points spent per attempt, on top of passive decay
    private const float Radius = 36f;      // visual canopy half-width — not collidable
    public const float TrunkRadius = 10f;  // small collidable trunk at the base

    // apple_tree.png is 16 wide × 32 tall — it's actually two adjacent
    // tiles from the source sheet stacked together (the canopy's top
    // lives in one tile, the trunk and the rest of the canopy in the
    // next); cropping just the bottom tile, which is what the first
    // pass here did, chopped the canopy's top clean off. apple_tree_
    // bare.png is a single self-contained 16×16 tile — genuinely
    // shorter, not another instance of the same mistake (checked the
    // same way: no opaque pixels touching a tile edge that would
    // suggest it continues into a neighboring one).
    private const float SpriteScale = (Radius * 2f) / 16f; // 4.5x — same width scale as before; height follows proportionally since both source images share the same 16px width
    private static readonly Vector2 FruitedSize = new(16, 32);
    private static readonly Vector2 BareSize = new(16, 16);

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
        AnchorSpriteAtBase();
    }

    // Not centered — anchored bottom-center instead, so the trunk's
    // visual base sits exactly at this node's origin (same place
    // TrunkRadius's collision circle and GetObstacleCircles() already
    // are), regardless of which of the two differently-sized textures
    // is currently showing. Without this, swapping fruited <-> bare on
    // depletion (a 32-tall image <-> a 16-tall one) would visibly jump
    // the tree up or down each time, since Sprite2D's default Centered
    // anchor is the image's middle, and the two images aren't the same
    // height.
    private void AnchorSpriteAtBase()
    {
        Vector2 size = _sprite.Texture == _fruitedTexture ? FruitedSize : BareSize;
        _sprite.Centered = false;
        _sprite.Offset = new Vector2(-size.X / 2f, -size.Y);
    }

    public Rect2 GetLocalBounds() => new(-Radius, -FruitedSize.Y * SpriteScale, Radius * 2f, FruitedSize.Y * SpriteScale);

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
        {
            _sprite.Texture = _bareTexture;
            AnchorSpriteAtBase();
        }
        data["apples_left"] = AppleCount;
        return new InteractResult(true, "ok", data);
    }
}
