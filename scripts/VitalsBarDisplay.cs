using Godot;

// A small floating readout above a character's or animal's head — up
// to three thin bars (Health always; Fatigue only when the owner
// actually has the concept, i.e. a human, not an animal; Hunger for
// both, since every living thing has that now — see Vitals/Animal's
// own Hunger). Purely a visual — nothing here feeds back into
// gameplay, and it has zero opinion about where the numbers come
// from; the owner (NPCActor/Animal) calls Refresh() with fresh
// fractions whenever its own vitals change.
//
// Toggleable via GameSettings.ShowVitalsBars, checked every frame
// rather than once — flipping the setting mid-game needs to hide/show
// every instance at once, and there's no central registry of "every
// display that currently exists" to notify individually; each one
// just watches the same static flag itself.
public partial class VitalsBarDisplay : Node2D
{
    private const float BarWidth = 34f;
    private const float BarHeight = 3f;
    private const float BarGap = 1.5f;
    private const float BgAlpha = 0.6f;

    private static readonly Color HealthColor = new(0.85f, 0.25f, 0.22f);
    private static readonly Color FatigueColor = new(0.35f, 0.55f, 0.9f);
    private static readonly Color HungerColor = new(0.85f, 0.65f, 0.2f);

    private float _health = 1f, _fatigue = 1f, _hunger = 1f;
    private bool _showFatigue;

    // fatigueFrac null means "this owner has no Fatigue concept at
    // all" (every wild animal) — draws two bars instead of three
    // rather than a fake full/empty one.
    public void Refresh(float healthFrac, float? fatigueFrac, float hungerFrac)
    {
        _health = Mathf.Clamp(healthFrac, 0f, 1f);
        _showFatigue = fatigueFrac.HasValue;
        _fatigue = Mathf.Clamp(fatigueFrac ?? 0f, 0f, 1f);
        _hunger = Mathf.Clamp(hungerFrac, 0f, 1f);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        bool wantVisible = GameSettings.ShowVitalsBars;
        if (Visible != wantVisible)
            Visible = wantVisible;
    }

    public override void _Draw()
    {
        float y = 0f;
        DrawBar(y, _health, HealthColor);
        y += BarHeight + BarGap;
        if (_showFatigue)
        {
            DrawBar(y, _fatigue, FatigueColor);
            y += BarHeight + BarGap;
        }
        DrawBar(y, _hunger, HungerColor);
    }

    private void DrawBar(float y, float frac, Color fillColor)
    {
        DrawRect(new Rect2(-BarWidth / 2f, y, BarWidth, BarHeight), new Color(0f, 0f, 0f, BgAlpha));
        if (frac > 0f)
            DrawRect(new Rect2(-BarWidth / 2f, y, BarWidth * frac, BarHeight), fillColor);
    }
}
