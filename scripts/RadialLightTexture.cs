using Godot;

// A plain white-to-transparent radial gradient — everything a soft
// circular PointLight2D needs, built in code rather than an asset file
// for something this simple. Extracted from PlayerCharacter's own
// VisibilityLight (the original, still its only caller for a while) so
// FirePit's own glow and NPCActor's torch light can share the exact
// same texture rather than each generating their own copy — the
// texture itself carries no color; every caller tints it via its own
// PointLight2D.Color instead (white for plain visibility, warm orange
// for fire/torchlight), so one shared shape genuinely serves all of
// them.
public static class RadialLightTexture
{
    public const int Size = 256;
    private static Texture2D _texture;

    public static Texture2D Get()
    {
        if (_texture != null)
            return _texture;

        var gradient = new Gradient();
        gradient.SetColor(0, Colors.White);
        gradient.SetColor(1, new Color(1f, 1f, 1f, 0f));

        _texture = new GradientTexture2D
        {
            Gradient = gradient,
            Width = Size,
            Height = Size,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
        };
        return _texture;
    }
}
