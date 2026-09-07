using Godot;

// Builds a SpriteFrames resource from assets/characters/characters.png at
// runtime rather than a hand-authored .tres — a SpriteFrames resource
// file has a fiddly internal format, and constructing it in code means
// there's nothing to get subtly wrong blind (no editor here to verify a
// hand-written resource actually parses the way intended). See
// assets/CREDITS.md for exactly where this sheet came from and its
// layout: a standard 4-direction character sheet — 4 columns (facing
// left/down/up/right, in that order) × 3 rows PER CHARACTER (a 3-frame
// walk cycle within that facing: neutral, mid-step, neutral), 16×16 per
// frame, no margin, 6 character color variants stacked vertically (18
// rows total).
//
// An earlier version of this class got the sheet's own axes backwards —
// it read across the 3 ROWS of one character as if they were 3 walk-
// animation frames of a single fixed-facing sprite, staying on one
// column throughout. That's the wrong axis entirely: rows are walk
// frames WITHIN a facing, columns are the actual facing. Treating a
// column-major sheet as row-major meant each "animation frame" was
// really a different DIRECTION's single pose, so the walk loop flashed
// between left/down/up/right facings every cycle — read as the
// character spinning while walking, not walking in a straight line.
//
// Used identically by NpcFactory (for NPCs) and PlayerCharacter (for
// itself) — same sheet, same builder, just a different variant index —
// consistent with every other "NPC and player share the same thing"
// pattern in this codebase.
public static class CharacterSpriteBuilder
{
    // Public — NPCActor references this directly to anchor the sprite at
    // the character's feet rather than duplicating the 16 as its own
    // magic number.
    public const int FrameSize = 16;
    private const int RowsPerCharacter = 3;

    // How many distinct character color variants the sheet actually has
    // — callers should pass variantIndex % VariantCount to always land
    // on a real character regardless of how many NPCs/players exist.
    public const int VariantCount = 6;

    // Column order in the sheet — left, down, up, right, matching the
    // source RPG Urban Pack tilemap's own column order exactly (see
    // assets/CREDITS.md), so no reshuffling was needed when cropping.
    private const int ColLeft = 0, ColDown = 1, ColUp = 2, ColRight = 3;

    // The four facing names every caller (NPCActor.UpdateSpriteFacing)
    // builds "walk_"/"idle_" animation names out of — kept in one place
    // so a typo in a direction string can't silently mismatch between
    // here and there.
    public const string Down = "down", Up = "up", Left = "left", Right = "right";

    private static Texture2D _sheet;
    private static Texture2D Sheet => _sheet ??= GD.Load<Texture2D>("res://assets/characters/characters.png");

    public static SpriteFrames Build(int variantIndex)
    {
        int rowBase = (((variantIndex % VariantCount) + VariantCount) % VariantCount) * RowsPerCharacter;

        var frames = new SpriteFrames();
        frames.RemoveAnimation("default"); // every new SpriteFrames starts with one; not using it

        AddDirection(frames, Left, ColLeft, rowBase);
        AddDirection(frames, Down, ColDown, rowBase);
        AddDirection(frames, Up, ColUp, rowBase);
        AddDirection(frames, Right, ColRight, rowBase);

        return frames;
    }

    // One direction's own idle + walk animation, both drawn from the
    // same column (that direction's facing) — idle is just row 0 (the
    // neutral "standing" pose row 2 also uses, bracketing the mid-step
    // pose in row 1), walk cycles through all 3 rows in order. Row order
    // 0,1,2 (not a ping-pong reorder, unlike the walk-sway approach this
    // replaces) already loops smoothly on its own: rows 0 and 2 are both
    // the same neutral pose, so the wrap from the last frame back to the
    // first is neutral-to-neutral, not a jump — only 0→1 and 1→2 move at
    // all, the same "contact, passing, contact" shape a real walk cycle
    // has.
    private static void AddDirection(SpriteFrames frames, string name, int col, int rowBase)
    {
        string idleAnim = "idle_" + name;
        string walkAnim = "walk_" + name;

#pragma warning disable CS0618 // SetAnimationLoop is obsolete in favor of SetAnimationLoopMode, whose enum type isn't resolving here — the deprecated overload still works and this API is unlikely to actually disappear from Godot 4.x
        frames.AddAnimation(idleAnim);
        frames.SetAnimationLoop(idleAnim, true);
        frames.SetAnimationSpeed(idleAnim, 2.0);
        frames.AddFrame(idleAnim, FrameAt(col, rowBase));

        frames.AddAnimation(walkAnim);
        frames.SetAnimationLoop(walkAnim, true);
        frames.SetAnimationSpeed(walkAnim, 6.0);
#pragma warning restore CS0618
        for (int r = 0; r < RowsPerCharacter; r++)
            frames.AddFrame(walkAnim, FrameAt(col, rowBase + r));
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
