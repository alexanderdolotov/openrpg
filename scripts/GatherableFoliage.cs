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

    private const float GatherExertion = 3f; // same as AppleTree's

    private static readonly Dictionary<string, Texture2D> _textureCache = new();
    private static readonly Color DepletedMultiplier = new(0.6f, 0.65f, 0.63f); // same grey AppleTree/FishingSpot already tint toward

    private Sprite2D _sprite;
    private Vector2 _textureSize;

    public override void _Ready()
    {
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

    public Rect2 GetLocalBounds() => new(-_textureSize.X * SpriteScale / 2f, -_textureSize.Y * SpriteScale, _textureSize.X * SpriteScale, _textureSize.Y * SpriteScale);

    public IEnumerable<(Vector2, float)> GetObstacleCircles()
    {
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
        actor.Inventory.Add(ItemName);
        if (Count <= 0)
            _sprite.Modulate = Tint * DepletedMultiplier;
        data[$"{ItemName}s_left"] = Count;
        return new InteractResult(true, "ok", data);
    }
}
