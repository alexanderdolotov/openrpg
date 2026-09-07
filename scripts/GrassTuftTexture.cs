using Godot;

// A small fan of grass blades — built in code for the same reason
// RadialLightTexture/TorchIconTexture are: nothing here to source or
// get subtly wrong blind with no editor to preview a hand-authored
// image against. GenerateGrassPatch used to reuse bush_plain.png
// (tinted green) for this, which read as a round bush blob rather than
// ground-level grass — this instead draws several thin blades of
// varying height, lean, and shade fanning up from one base, which
// reads as a tuft at the small scale grass patches render at.
public static class GrassTuftTexture
{
    public const int Width = 12;
    public const int Height = 10;
    private static Texture2D _texture;

    private readonly record struct Blade(float BaseX, float TipX, float TipY, Color Color);

    public static Texture2D Get()
    {
        if (_texture != null)
            return _texture;

        var image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);

        var darkGreen = new Color(0.25f, 0.5f, 0.18f);
        var midGreen = new Color(0.35f, 0.62f, 0.22f);
        var lightGreen = new Color(0.5f, 0.75f, 0.3f);

        // Bases spread across the bottom, tips fanning outward and
        // upward at slightly different heights — an even fan would read
        // as a static icon rather than an actual clump of grass.
        Blade[] blades =
        {
            new(2f, 0f, 3f, darkGreen),
            new(3f, 1f, 0f, midGreen),
            new(5f, 4f, 1f, lightGreen),
            new(6f, 6f, 0f, midGreen),
            new(7f, 8f, 1f, lightGreen),
            new(9f, 10f, 0f, midGreen),
            new(10f, 12f, 3f, darkGreen),
        };

        float baseY = Height - 1;
        foreach (var blade in blades)
        {
            int steps = Mathf.Max(1, (int)Mathf.Ceil(baseY - blade.TipY));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                int x = Mathf.RoundToInt(Mathf.Lerp(blade.BaseX, blade.TipX, t));
                int y = Mathf.RoundToInt(Mathf.Lerp(baseY, blade.TipY, t));
                if (x >= 0 && x < Width && y >= 0 && y < Height)
                    image.SetPixel(x, y, blade.Color);
            }
        }

        _texture = ImageTexture.CreateFromImage(image);
        return _texture;
    }
}
