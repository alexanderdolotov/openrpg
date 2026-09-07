using Godot;

// "Wolves will hunt rabbits. Wolves will also attack humans... if wolf
// is full, he won't initiate attack, but will always defend if
// attacked. Wolves DO NOT attack other wolves, but may attack a bear
// if starving." Priority order below is exactly that sentence: defend
// (regardless of hunger) > full means never initiate > hunt a rabbit >
// starving means go after a human or a bear instead > otherwise just
// wander.
public partial class Wolf : Animal
{
    protected override float MoveSpeed => 110f;
    protected override float DetectionRadius => 260f;
    protected override int UnarmedDamage => 8;

    // A real predator's edge, not Animal's own base defaults (which
    // every species left unset before this — see Rabbit's own
    // constructor comment for why that mattered).
    public Wolf() { Strength = 12; Dexterity = 13; MaxHealth = 35f; }

    private const float FullThreshold = 70f; // at or above this, never initiates anything
    private const float StarvingThreshold = 25f; // below this AND no rabbit found, will go after a human or bear
    private const float DefendWindow = 3f; // how long "just got hit" keeps overriding everything else

    // Drawn, not sprited — same reasoning as Rabbit's own header.
    private static readonly Color Body = new(0.45f, 0.46f, 0.5f);
    private static readonly Color Dark = new(0.3f, 0.31f, 0.35f);

    public override void _Draw()
    {
        DrawCircle(new Vector2(2f, 0f), 13f, Body); // body, elongated toward the front
        DrawCircle(new Vector2(-10f, -3f), 8f, Body); // head
        Vector2[] earL = { new(-16f, -8f), new(-18f, -18f), new(-12f, -10f) };
        Vector2[] earR = { new(-8f, -9f), new(-8f, -19f), new(-4f, -11f) };
        DrawColoredPolygon(earL, Dark);
        DrawColoredPolygon(earR, Dark);
        Vector2[] tail = { new(14f, -2f), new(24f, -8f), new(18f, 4f) };
        DrawColoredPolygon(tail, Dark);
        DrawCircle(new Vector2(-16f, -3f), 2f, Dark); // snout tip
    }

    protected override void DecideBehavior(float delta)
    {
        // Always defend if attacked — the one thing that overrides
        // "full wolves don't initiate," since defending isn't
        // initiating.
        if (TimeSinceAttacked < DefendWindow && LastAttacker is Node2D attacker && IsInstanceValid(attacker))
        {
            SetChaseOrAttack(attacker);
            return;
        }

        if (Hunger >= FullThreshold)
        {
            _state = State.Wandering;
            return;
        }

        Rabbit prey = FindNearestAnimal<Rabbit>(DetectionRadius);
        if (prey != null)
        {
            SetChaseOrAttack(prey);
            return;
        }

        if (Hunger < StarvingThreshold)
        {
            NPCActor human = FindNearestHuman(DetectionRadius);
            if (human != null) { SetChaseOrAttack(human); return; }

            Bear bear = FindNearestAnimal<Bear>(DetectionRadius);
            if (bear != null) { SetChaseOrAttack(bear); return; }
        }

        _state = State.Wandering;
    }

    protected override void OnKilled(ICombatant victim)
    {
        if (victim is Rabbit) Hunger = Mathf.Min(MaxHunger, Hunger + 60f);
        else if (victim is Bear) Hunger = Mathf.Min(MaxHunger, Hunger + 80f);
    }
}
