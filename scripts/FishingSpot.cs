using Godot;

// The fishing equivalent of AppleTree — same IInteractable contract,
// same shape of rules (depleted, hands full), different action id and
// resource pool. Nothing here or anywhere else says which NPC is "the
// fisher" — this spot is exposed identically to every NPC's tool
// schema, and whichever one actually goes for it is purely a call the
// LLM makes from personality/backstory framing.
public partial class FishingSpot : Node2D, IInteractable, IHasVisualBounds
{
    [Export] public int FishCount = 3;

    private const float GatherExertion = 4f; // a bit more than picking an apple — fishing takes more out of you
    private const float Radius = 24f;
    private const float SpriteScale = (Radius * 2f) / 16f; // 16×16 source tile -> 48px, matching the old drawn circle's footprint

    // Depleted isn't a separate cropped asset (unlike AppleTree's bare
    // tree) — just a gray tint over the same water tile, cheaper than
    // sourcing and cropping a second texture for one desaturated state.
    private static readonly Color DepletedTint = new(0.6f, 0.65f, 0.63f);

    private static Texture2D _waterTexture;
    private Sprite2D _sprite;

    public override void _Ready()
    {
        _waterTexture ??= GD.Load<Texture2D>("res://assets/world/water.png");
        _sprite = new Sprite2D
        {
            Texture = _waterTexture,
            Scale = new Vector2(SpriteScale, SpriteScale),
            TextureFilter = TextureFilterEnum.Nearest,
            Modulate = FishCount > 0 ? Colors.White : DepletedTint,
        };
        AddChild(_sprite);
    }

    public Rect2 GetLocalBounds() => new(-Radius, -Radius, Radius * 2f, Radius * 2f);

    public InteractResult TryInteract(NPCActor actor, string actionId)
    {
        if (actionId != "catch_fish")
            return new InteractResult(false, "wrong_action");
        if (FishCount <= 0)
            return new InteractResult(false, "depleted");

        // Same low-bar-but-real check as AppleTree — a fumble here is a
        // fish that got away, not a failure to find one.
        var check = SkillCheck.Roll(actor.Stats.DexterityMod, DifficultyClass.Gather);
        var data = check.ToData("dexterity");
        actor.Vitals.Exert(GatherExertion);
        if (!check.Success)
            return new InteractResult(false, "fumbled", data);

        FishCount--;
        actor.Inventory.Add("fish");
        if (FishCount <= 0)
            _sprite.Modulate = DepletedTint;
        data["fish_left"] = FishCount;
        return new InteractResult(true, "ok", data);
    }
}
