using Godot;

// A tiny held-torch sprite — a brown handle plus a round two-tone flame,
// built in code rather than an asset file for the same reason
// RadialLightTexture is: there's nothing this simple to get subtly wrong
// blind with no editor here to preview a hand-authored image against.
// The 16×16 character sheet (CharacterSpriteBuilder) has no
// holding-a-torch frame of its own — Kenney's RPG Urban Pack crop this
// project uses never drew one — so this is composited on top of the
// character sprite as its own child node instead, shown/hidden by
// NPCActor alongside its TorchLight, off the same Inventory.Has("torch")
// check.
public static class TorchIconTexture
{
    // Deliberately small relative to CharacterSpriteBuilder.FrameSize
    // (16): this sits in the same pre-scale pixel space as the
    // character sprite itself (NPCActor parents it under _sprite, at a
    // hand-height Position — see TorchHandOffset), so a torch as tall as
    // the character's own 16px frame would tower a full head-height
    // above them once held. 9px reaches from hand to just over the
    // head, the way a carried torch actually would.
    public const int Width = 6;
    public const int Height = 9;
    private static Texture2D _texture;

    public static Texture2D Get()
    {
        if (_texture != null)
            return _texture;

        var image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);

        // Handle — a narrow stick along the bottom half, left untouched
        // (transparent) above so the flame reads as sitting on top of
        // it rather than inside a box.
        var handleColor = new Color(0.35f, 0.22f, 0.1f);
        for (int y = 4; y < Height; y++)
        {
            image.SetPixel(2, y, handleColor);
            image.SetPixel(3, y, handleColor);
        }

        // Flame — a small round blob, brightest at its core, same warm
        // orange/yellow pairing FirePit and TorchLight already use for
        // fire elsewhere in this project.
        var flameOuter = new Color(1f, 0.45f, 0.05f);
        var flameInner = new Color(1f, 0.85f, 0.35f);
        var center = new Vector2(2.5f, 2f);
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                float distance = new Vector2(x + 0.5f, y).DistanceTo(center);
                if (distance < 1.9f)
                    image.SetPixel(x, y, distance < 0.9f ? flameInner : flameOuter);
            }
        }

        _texture = ImageTexture.CreateFromImage(image);
        return _texture;
    }
}
