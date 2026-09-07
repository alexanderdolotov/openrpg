using Godot;

// "Wolves will hunt rabbits. Wolves will also attack humans... if wolf
// is full, he won't initiate attack, but will always defend if
// attacked. Wolves DO NOT attack other wolves, but may attack a bear
// if starving." Priority order below is exactly that sentence: defend
// (regardless of hunger) > full means never initiate > hunt a rabbit >
// starving means go after a human or a bear instead (but still sizes
// up the fight first — see Animal.WinnableFight — unless truly
// desperate, in which case "wolves become very aggressive: will fight
// even stronger animals than them") > otherwise just wander.
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
    private const float StarvingThreshold = 25f; // below this AND no rabbit found, will go after a human or bear IT THINKS it can actually take (see WinnableFight)
    private const float DesperateThreshold = 10f; // below THIS, no more sizing up — "will fight even stronger animals than them"
    private const float DefendWindow = 3f; // how long "just got hit" keeps overriding everything else

    // Stalk speed while hunting a rabbit that hasn't noticed this wolf
    // yet — a slow, quiet approach, not a dead sprint, but still
    // comfortably faster than a rabbit's own idle wandering (its
    // State.Wandering moves at MoveSpeed*0.5 ≈ 47) — a first pass at
    // 0.4 (44) was actually SLOWER than that, verified empirically:
    // the wolf couldn't reliably close distance on a rabbit that
    // wasn't even fleeing yet, just wandering obliviously, since a
    // straight pursuit at a slower speed than its target's own
    // meander never guarantees catching up. Once the rabbit IS
    // fleeing (it noticed — see Rabbit.NoticesWolf), there's no more
    // point being quiet about it, so it's a full-speed chase from
    // there — a chase a healthy, alert rabbit still wins outright; the
    // whole point of stalking is closing the gap BEFORE that chase
    // ever starts.
    private const float StalkSpeedFactor = 0.7f;

    protected override float ChaseSpeedFor(Node2D target) =>
        target is Rabbit r && r.CurrentState != State.Fleeing ? MoveSpeed * StalkSpeedFactor : MoveSpeed;

    // Drawn, not sprited — same reasoning as Rabbit's own header.
    private static readonly Color Body = new(0.45f, 0.46f, 0.5f);
    private static readonly Color Dark = new(0.3f, 0.31f, 0.35f);

    public override void _Draw()
    {
        DrawSetTransform(LungeOffset); // see Animal.PlayLunge's own header
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
            SetChaseOrAttack(attacker, "defending itself");
            return;
        }

        // Already mid-rest and not fully recovered yet — keep resting,
        // full stop, until it either finishes or something attacks
        // (caught above, since that check runs first every tick
        // regardless). See IsCommittedToResting's own header for the
        // bug this fixes.
        if (IsCommittedToResting) return;

        if (Hunger >= FullThreshold)
        {
            if (NeedsRest) { SetResting(); return; }
            _state = State.Wandering;
            return;
        }

        Rabbit prey = FindNearestAnimal<Rabbit>(DetectionRadius);
        if (prey != null)
        {
            SetChaseOrAttack(prey, prey.CurrentState == State.Fleeing ? null : "stalking, unnoticed");
            return;
        }

        if (Hunger < StarvingThreshold)
        {
            // Merely starving still sizes up the fight first; truly
            // desperate skips that entirely — see WinnableFight's own
            // header and DesperateThreshold above.
            bool desperate = Hunger < DesperateThreshold;

            string reason = desperate ? "desperate with hunger" : "starving";
            NPCActor human = FindNearestHuman(DetectionRadius);
            if (human != null && (desperate || WinnableFight(human))) { SetChaseOrAttack(human, reason); return; }

            Bear bear = FindNearestAnimal<Bear>(DetectionRadius);
            if (bear != null && (desperate || WinnableFight(bear))) { SetChaseOrAttack(bear, reason); return; }
        }

        // Nothing more urgent to do — hunt/eat/defend all already
        // failed to apply above — so if it's actually tired, rest
        // instead of just wandering aimlessly.
        if (NeedsRest)
        {
            SetResting();
            return;
        }

        _state = State.Wandering;
    }

    protected override void OnKilled(ICombatant victim)
    {
        if (victim is Rabbit) Hunger = Mathf.Min(MaxHunger, Hunger + 60f);
        else if (victim is Bear) Hunger = Mathf.Min(MaxHunger, Hunger + 80f);
    }
}
