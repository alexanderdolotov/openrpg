using Godot;

// "Bears will prefer berries, unless starving, then will attack
// anything." Priority: defend if attacked > full means never initiate
// > berries first, always, while any are in reach > starving (and no
// berries found) means attack whatever's nearest it thinks it can
// actually take (Animal.WinnableFight) — human, wolf, or rabbit, no
// preference among them — or, once truly desperate, whatever's nearest
// regardless of the odds.
public partial class Bear : Animal
{
    protected override float MoveSpeed => 55f; // slower than a wolf, but hits much harder
    protected override float DetectionRadius => 200f;
    protected override int UnarmedDamage => 14;

    // Very strong, tanky, and a bit clumsy — not Animal's own base
    // defaults (which every species left unset before this — see
    // Rabbit's own constructor comment for why that mattered).
    public Bear() { Strength = 18; Dexterity = 7; MaxHealth = 70f; }

    private const float FullThreshold = 75f;
    private const float StarvingThreshold = 20f; // below this AND no berries found, attacks whatever's nearest it THINKS it can take (see WinnableFight) — rarely declines, being the strongest of the three species
    private const float DesperateThreshold = 8f; // below THIS, no more sizing up at all
    private const float DefendWindow = 3f;

    // Drawn, not sprited — same reasoning as Rabbit's own header.
    private static readonly Color Body = new(0.4f, 0.27f, 0.16f);
    private static readonly Color Dark = new(0.28f, 0.18f, 0.1f);

    public override void _Draw()
    {
        DrawSetTransform(LungeOffset); // see Animal.PlayLunge's own header
        DrawCircle(new Vector2(1f, 2f), 17f, Body); // big, bulky body
        DrawCircle(new Vector2(-13f, -4f), 10f, Body); // head
        DrawCircle(new Vector2(-19f, -12f), 3.5f, Body); // ears
        DrawCircle(new Vector2(-8f, -12f), 3.5f, Body);
        DrawCircle(new Vector2(-19f, -3f), 2.5f, Dark); // snout
    }

    protected override void DecideBehavior(float delta)
    {
        if (TimeSinceAttacked < DefendWindow && LastAttacker is Node2D attacker && IsInstanceValid(attacker))
        {
            SetChaseOrAttack(attacker, "defending itself");
            return;
        }

        // See Wolf's own identical check for the bug this fixes —
        // IsCommittedToResting's header has the full reasoning.
        if (IsCommittedToResting) return;

        if (Hunger >= FullThreshold)
        {
            if (NeedsRest) { SetResting(); return; }
            _state = State.Wandering;
            return;
        }

        // Berries preferred (see the point-value split below) — grass
        // only when no bush is within range. Same fallback Rabbit's own
        // DecideBehavior uses now, and the same reason it exists: a
        // bear that can't find a bush shouldn't have to jump straight
        // to StarvingThreshold/attacking someone when there's a
        // perfectly real, common food source it just isn't checking.
        GatherableFoliage bush = FindNearestFoliage(World.BerryBushes, DetectionRadius);
        GatherableFoliage food = bush ?? FindNearestFoliage(World.GrassPatches, DetectionRadius);
        if (food != null)
        {
            float dist = GlobalPosition.DistanceTo(food.GlobalPosition);
            if (dist <= EatRange)
            {
                if (food.AnimalEat())
                    Hunger = Mathf.Min(MaxHunger, Hunger + (food == bush ? 40f : 15f));
                _state = State.Wandering;
            }
            else
            {
                TargetNode = food;
                _state = State.SeekingFood;
            }
            return;
        }

        if (Hunger < StarvingThreshold)
        {
            bool desperate = Hunger < DesperateThreshold;

            string reason = desperate ? "desperate with hunger" : "starving, no berries nearby";
            NPCActor human = FindNearestHuman(DetectionRadius);
            if (human != null && (desperate || WinnableFight(human))) { SetChaseOrAttack(human, reason); return; }

            Wolf wolf = FindNearestAnimal<Wolf>(DetectionRadius);
            if (wolf != null && (desperate || WinnableFight(wolf))) { SetChaseOrAttack(wolf, reason); return; }

            Rabbit rabbit = FindNearestAnimal<Rabbit>(DetectionRadius);
            if (rabbit != null) { SetChaseOrAttack(rabbit, reason); return; } // never a real fight either way — no size-up needed
        }

        // Nothing more urgent to do — rest instead of just wandering
        // aimlessly if it's actually tired.
        if (NeedsRest)
        {
            SetResting();
            return;
        }

        _state = State.Wandering;
    }

    protected override void OnKilled(ICombatant victim)
    {
        if (victim is Rabbit || victim is Wolf)
            Hunger = Mathf.Min(MaxHunger, Hunger + 50f);
    }
}
