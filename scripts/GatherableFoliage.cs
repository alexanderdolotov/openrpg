using Godot;
using System.Collections.Generic;

// Shared "walk up, gather until empty, then just sit there tinted"
// mechanic — the same shape FishingSpot already uses (one texture, a
// grey tint once depleted, no separate depleted asset to source),
// generalized so pine (pinecones) and each berry bush variant
// (blueberry/blackberry/raspberry — same source sprite, different
// Modulate tint and item name, since the sheet this project's other
// art comes from only has one berry-dot color, not three — see
// assets/CREDITS.md) don't each reimplement the identical pick/
// deplete/exert/skill-check dance AppleTree/FishingSpot already
// established. AppleTree itself stays separate — its fruited/bare
// asset swap is a genuinely different, more bespoke shape (two
// differently-SIZED textures, not just a tint), not worth forcing in
// here too.
public partial class GatherableFoliage : StaticBody2D, IInteractable, IHasVisualBounds, IObstacle
{
    [Export] public int Count = 3;
    [Export] public string ActionId = ""; // e.g. "gather_pinecone", "gather_berry"
    [Export] public string ItemName = ""; // e.g. "pinecone", "blueberry"
    [Export] public string TexturePath = "";
    [Export] public float SpriteScale = 2.5f;
    [Export] public float TrunkRadius = 10f;
    [Export] public Color Tint = Colors.White; // lets one shared sprite stand in for several distinct-feeling items

    // True for ground cover that every character should be able to
    // walk straight over — grass, not the pinecone/berry bushes this
    // same class also renders, which should keep blocking like any
    // other bush. Skips both the real CollisionShape2D (so movement
    // physics doesn't stop at it) and this node's own contribution to
    // IObstacle's pathfinding grid (so NPCs don't detour around it
    // either) — "no boundaries" means both, not just one.
    [Export] public bool Walkable = false;

    // 0 (the default) means never — a berry bush or pinecone tree
    // stays a real, permanently-managed resource once picked clean,
    // same as always. Grass opts in with a real number: it went
    // through a Count=9999 "basically unlimited" patch first (see git
    // history) after every rabbit AND bear on the map turned out to be
    // drawing from the same non-renewing Count=2 pool — 8 patches × 2
    // bites was a hard cap of 16 meals for the entire wildlife
    // population, ever. Actual regrowth is the more honest fix than
    // "unlimited": depleted for a while reads as a real, finite patch;
    // silently uncapped doesn't.
    [Export] public float RegenSeconds = 0f;

    private const float GatherExertion = 3f; // same as AppleTree's

    private static readonly Dictionary<string, Texture2D> _textureCache = new();
    private static readonly Color DepletedMultiplier = new(0.6f, 0.65f, 0.63f); // same grey AppleTree/FishingSpot already tint toward

    private Sprite2D _sprite;
    private Vector2 _textureSize;

    // Count itself only ever counts down (TryInteract/AnimalEat) —
    // _maxCount is what a full regen restocks back up to, captured once
    // here rather than re-derived, since Count is the same field being
    // drained.
    private int _maxCount;
    private float _regenTimer;

    public override void _Ready()
    {
        _maxCount = Count;

        // Skips this node's own _Process call entirely (not just an
        // early-return inside it) for the common case — a pinecone
        // tree never opts into regen, and there can be a lot of these
        // by the time a long session's procedural growth is done. No
        // reason to make Godot's scheduler visit every one of them 60
        // times a second just to immediately bail on RegenSeconds<=0.
        SetProcess(RegenSeconds > 0f);

        if (!Walkable)
            AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = TrunkRadius } });

        if (!_textureCache.TryGetValue(TexturePath, out Texture2D texture))
        {
            texture = GD.Load<Texture2D>(TexturePath);
            _textureCache[TexturePath] = texture;
        }
        _textureSize = texture.GetSize();

        _sprite = new Sprite2D
        {
            Texture = texture,
            Scale = new Vector2(SpriteScale, SpriteScale),
            TextureFilter = TextureFilterEnum.Nearest,
            Centered = false,
            Offset = new Vector2(-_textureSize.X / 2f, -_textureSize.Y), // bottom-anchored, same reasoning as DecorativeFoliage/AppleTree
            Modulate = Count > 0 ? Tint : Tint * DepletedMultiplier,
        };
        AddChild(_sprite);
    }

    // Ticks the regrowth clock — reset to 0 by every successful pick
    // (TryInteract/AnimalEat below), so continuous grazing/gathering
    // keeps deferring it, the same shape FirePit's own LitDuration/
    // TorchDuration countdowns already use elsewhere in this project.
    // A no-op whenever RegenSeconds is 0 (the default) or Count's
    // already back at full, so this costs nothing for the vast
    // majority of GatherableFoliage instances that never opt in.
    public override void _Process(double delta)
    {
        if (RegenSeconds <= 0f || Count >= _maxCount)
            return;

        _regenTimer += (float)delta;
        if (_regenTimer < RegenSeconds)
            return;

        Count = _maxCount;
        _regenTimer = 0f;
        _sprite.Modulate = Tint;
    }

    public Rect2 GetLocalBounds() => new(-_textureSize.X * SpriteScale / 2f, -_textureSize.Y * SpriteScale, _textureSize.X * SpriteScale, _textureSize.Y * SpriteScale);

    public IEnumerable<(Vector2, float)> GetObstacleCircles()
    {
        if (Walkable)
            yield break;
        yield return (Vector2.Zero, TrunkRadius);
    }

    public InteractResult TryInteract(NPCActor actor, string actionId)
    {
        if (actionId != ActionId)
            return new InteractResult(false, "wrong_action");
        if (Count <= 0)
            return new InteractResult(false, "depleted");

        // Same low-bar-but-real check as AppleTree/FishingSpot.
        var check = SkillCheck.Roll(actor.Stats.DexterityMod, DifficultyClass.Gather);
        var data = check.ToData("dexterity");
        actor.Vitals.Exert(GatherExertion);
        if (!check.Success)
            return new InteractResult(false, "fumbled", data);

        Count--;
        _regenTimer = 0f; // still being picked from — defers regrowth, same as AnimalEat below
        actor.Inventory.Add(ItemName);
        if (Count <= 0)
            _sprite.Modulate = Tint * DepletedMultiplier;
        data[$"{ItemName}s_left"] = Count;
        return new InteractResult(true, "ok", data);
    }

    // A wild animal eating directly — no roll, no Inventory, no
    // IInteractable dance (that whole contract is built around a human
    // actor). Just "is there anything left," and if so, one unit gone.
    // Bear's/Rabbit's own DecideBehavior() are the only callers today.
    public bool AnimalEat()
    {
        if (Count <= 0) return false;
        Count--;
        _regenTimer = 0f; // still being grazed — defers regrowth, same as TryInteract above
        if (Count <= 0)
            _sprite.Modulate = Tint * DepletedMultiplier;
        return true;
    }
}
