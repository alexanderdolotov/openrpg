using Godot;

// Builds a SpriteFrames resource from assets/characters/characters.png at
// runtime rather than a hand-authored .tres — a SpriteFrames resource
// file has a fiddly internal format, and constructing it in code means
// there's nothing to get subtly wrong blind (no editor here to verify a
// hand-written resource actually parses the way intended). See
// assets/CREDITS.md for exactly where this sheet came from and its
// layout: 3 walk-frame columns × 18 rows, 3 rows per character, 16×16
// per frame, no margin.
//
// Used identically by NpcFactory (for NPCs) and PlayerCharacter (for
// itself) — same sheet, same builder, just a different variant index —
// consistent with every other "NPC and player share the same thing"
// pattern in this codebase.
public static class CharacterSpriteBuilder
{
    private const int FrameSize = 16;
    private const int FramesPerRow = 3;
    private const int RowsPerCharacter = 3;

    // How many distinct character color variants the sheet actually has
    // — callers should pass variantIndex % VariantCount to always land
    // on a real character regardless of how many NPCs/players exist.
    public const int VariantCount = 6;

    private static Texture2D _sheet;
    private static Texture2D Sheet => _sheet ??= GD.Load<Texture2D>("res://assets/characters/characters.png");

    public static SpriteFrames Build(int variantIndex)
    {
        int row = (((variantIndex % VariantCount) + VariantCount) % VariantCount) * RowsPerCharacter;

        var frames = new SpriteFrames();
        frames.RemoveAnimation("default"); // every new SpriteFrames starts with one; not using it

#pragma warning disable CS0618 // SetAnimationLoop is obsolete in favor of SetAnimationLoopMode, whose enum type isn't resolving here — the deprecated overload still works and this API is unlikely to actually disappear from Godot 4.x
        frames.AddAnimation("idle");
        frames.SetAnimationLoop("idle", true);
        frames.SetAnimationSpeed("idle", 2.0);
        frames.AddFrame("idle", FrameAt(0, row));

        frames.AddAnimation("walk");
        frames.SetAnimationLoop("walk", true);
        frames.SetAnimationSpeed("walk", 6.0);
#pragma warning restore CS0618
        for (int col = 0; col < FramesPerRow; col++)
            frames.AddFrame("walk", FrameAt(col, row));

        return frames;
    }

    private static AtlasTexture FrameAt(int col, int row)
    {
        return new AtlasTexture
        {
            Atlas = Sheet,
            Region = new Rect2(col * FrameSize, row * FrameSize, FrameSize, FrameSize),
        };
    }
}
