using Godot;
using System.Collections.Generic;

// Solid, purely decorative foliage — no picking, nothing to interact
// with, just something to walk around. The "standalone" tree/bush
// variety (oak, the bare/dead tree, a plain bush — see Main.BuildWorld
// and Main.GenerateContentAt for where each is actually placed) is all
// just this one class configured with different [Export] params —
// there's nothing to reuse or duplicate about their BEHAVIOR, only
// their art and size, so three near-identical wrapper classes would
// buy nothing over one data-driven one.
public partial class DecorativeFoliage : StaticBody2D, IHasVisualBounds, IObstacle
{
    [Export] public string TexturePath = "";
    [Export] public float SpriteScale = 4.5f;
    [Export] public float TrunkRadius = 10f; // small collidable base; the canopy itself isn't solid, same call AppleTree made

    // Keyed by path, not shared with GatherableFoliage's own cache —
    // two small static caches cost nothing and keep each class simple,
    // rather than threading a shared cache object through both.
    private static readonly Dictionary<string, Texture2D> _textureCache = new();
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

        // Bottom-anchored, same reasoning as AppleTree's own
        // AnchorSpriteAtBase() — this node's own Position is where the
        // trunk actually touches the ground, which is what Y-sort
        // compares and what TrunkRadius's collision circle is centered
        // on; anchoring the sprite there instead of at its visual
        // center keeps all three in agreement regardless of the
        // source texture's height.
        var sprite = new Sprite2D
        {
            Texture = texture,
            Scale = new Vector2(SpriteScale, SpriteScale),
            TextureFilter = TextureFilterEnum.Nearest,
            Centered = false,
            Offset = new Vector2(-_textureSize.X / 2f, -_textureSize.Y),
        };
        AddChild(sprite);
    }

    public Rect2 GetLocalBounds() => new(-_textureSize.X * SpriteScale / 2f, -_textureSize.Y * SpriteScale, _textureSize.X * SpriteScale, _textureSize.Y * SpriteScale);

    public IEnumerable<(Vector2, float)> GetObstacleCircles()
    {
        yield return (Vector2.Zero, TrunkRadius);
    }
}
