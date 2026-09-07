using Godot;

// Small, shared "something just happened in a fight" helpers — a
// floating damage/miss number, and a quick lunge toward whoever's
// being hit. Used by both NPCActor.ProcessAttempting's "attack" case
// and Animal.TryAttack(), so every attack in the game (human or
// animal, on either side) reads as something actually happening on
// screen, not just numbers quietly changing underneath.
public static class CombatVisuals
{
    // A floating number/word that rises and fades over about a
    // second, shown above whoever just got hit (or missed). Spawned
    // as a sibling of the attacking character (same parent — every
    // character in this game sits directly under the world layer, see
    // NpcFactory/Main's own Y-sort notes), not a child of the
    // defender: a killing blow QueueFree()s the defender the same
    // frame this fires, and a child would get cut off along with it.
    public static void ShowFloatingText(Node parent, Vector2 worldPosition, string text, Color color)
    {
        if (parent == null) return;

        var label = new Label
        {
            Text = text,
            Position = worldPosition + new Vector2(-10f, -52f),
            ZIndex = 200, // above every character/foliage sprite, regardless of Y-sort
        };
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 3);
        parent.AddChild(label);

        Vector2 start = label.Position;
        Vector2 end = start + new Vector2(0f, -34f);
        Tween tween = label.CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(label, "position", end, 0.9);
        tween.TweenProperty(label, "modulate:a", 0.0, 0.6).SetDelay(0.3);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(label)) label.QueueFree();
        }));
    }

    public static void ShowDamage(Node parent, Vector2 worldPosition, int amount) =>
        ShowFloatingText(parent, worldPosition, $"-{amount}", new Color(0.95f, 0.3f, 0.25f));

    public static void ShowMiss(Node parent, Vector2 worldPosition) =>
        ShowFloatingText(parent, worldPosition, "miss", new Color(0.85f, 0.85f, 0.85f));

    // A quick lunge toward the target and back — the closest thing to
    // an attack animation without dedicated attack-frame art (the
    // character sheet only has idle/walk frames — see
    // CharacterSpriteBuilder's own header comment). Offsets a VISUAL
    // node's own Position (a character's AnimatedSprite2D child, for
    // NPCActor/PlayerCharacter — see NPCActor.PlayAttackLunge) via a
    // real Godot Tween, never the physics body itself, so this can
    // never fight MoveAndSlide() or collision. Animal has its own
    // separate, non-Tween version (see Animal.PlayLunge) since it has
    // no such child node to offset — it draws itself directly.
    public static void PlayLunge(Node2D visual, Vector2 towardWorldPosition)
    {
        if (visual == null) return;
        Vector2 direction = towardWorldPosition - visual.GlobalPosition;
        if (direction.LengthSquared() < 1f) return;

        Vector2 origin = visual.Position;
        Vector2 lunged = origin + direction.Normalized() * 6f;

        Tween tween = visual.CreateTween();
        tween.TweenProperty(visual, "position", lunged, 0.12);
        tween.TweenProperty(visual, "position", origin, 0.18);
    }
}
