using Godot;

// Gradual day/night lighting — extends Main's existing CanvasModulate
// "dim the whole world to a flat baseline" primitive (see Main.
// BuildLighting's own comment, which explicitly called this out as the
// next step: "a lower DimColor after dark") with a smoothly-cycling
// color instead of a fixed one: bright and neutral at midday, darker
// and distinctly blue-purple at midnight, continuously blended between
// the two rather than snapping between two fixed states.
//
// A child of _worldLayer (Pausable), not Main itself (Always) — the
// day/night clock needs to freeze along with everything else while the
// game is paused, the same way NPCs/animals/timers already do, not
// keep ticking along in the background. CanvasModulate's effect on the
// world canvas doesn't depend on exactly where in the tree it sits
// (it's a global modulate for the whole default canvas, not scoped to
// its own children) — moving it under _worldLayer only changes which
// ProcessMode it inherits, not what it visually affects.
public partial class DayNightCycle : CanvasModulate
{
    // A full day-night cycle, in real seconds — 5 minutes of day, 5 of
    // night (the cosine curve below spends roughly the first/last
    // quarter of each half near its own peak/trough and the middle
    // half actually transitioning, so this reads as "5 minutes of
    // daylight, 5 of night," not literally a 5-minute sunrise).
    public const float DayLengthSeconds = 600f; // 10 real minutes total

    // Main's own original, already-tuned DimColor — kept as the
    // brightest point of the cycle (midday) so daytime gameplay looks
    // exactly like it always has; only night gets genuinely new.
    private static readonly Color DayColor = new(0.55f, 0.55f, 0.62f);

    // Darker AND a distinctly different hue, not just a dimmer version
    // of the same color — shifted toward blue-purple by pulling red
    // down further than blue, not just scaling everything down evenly.
    private static readonly Color NightColor = new(0.13f, 0.14f, 0.32f);

    private float _elapsed;

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        float phase = _elapsed % DayLengthSeconds / DayLengthSeconds; // 0..1, 0 (and 1) = midday

        // Cosine, not linear — the same shape real daylight roughly
        // has: slowest right at noon and midnight, fastest through the
        // dawn/dusk transitions in between, so this reads as a smooth
        // gradual shift rather than a mechanical ramp.
        float t = (Mathf.Cos(phase * Mathf.Tau) + 1f) / 2f; // 1 at midday, 0 at midnight
        Color = NightColor.Lerp(DayColor, t);
    }
}
