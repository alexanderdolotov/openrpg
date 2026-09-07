using Godot;

// "Bears will prefer berries, unless starving, then will attack
// anything." Priority: defend if attacked > full means never initiate
// > berries first, always, while any are in reach > starving (and no
// berries found) means attack whatever's nearest — human, wolf, or
// rabbit, no preference among them.
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
    private const float StarvingThreshold = 20f;
    private const float DefendWindow = 3f;

    // Drawn, not sprited — same reasoning as Rabbit's own header.
    private static readonly Color Body = new(0.4f, 0.27f, 0.16f);
    private static readonly Color Dark = new(0.28f, 0.18f, 0.1f);

    public override void _Draw()
    {
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
            SetChaseOrAttack(attacker);
            return;
        }

        if (Hunger >= FullThreshold)
        {
            _state = State.Wandering;
            return;
        }

        GatherableFoliage bush = FindNearestFoliage(World.BerryBushes, DetectionRadius);
        if (bush != null)
        {
            float dist = GlobalPosition.DistanceTo(bush.GlobalPosition);
            if (dist <= EatRange)
            {
                if (bush.AnimalEat())
                    Hunger = Mathf.Min(MaxHunger, Hunger + 40f);
                _state = State.Wandering;
            }
            else
            {
                TargetNode = bush;
                _state = State.SeekingFood;
            }
            return;
        }

        if (Hunger < StarvingThreshold)
        {
            NPCActor human = FindNearestHuman(DetectionRadius);
            if (human != null) { SetChaseOrAttack(human); return; }

            Wolf wolf = FindNearestAnimal<Wolf>(DetectionRadius);
            if (wolf != null) { SetChaseOrAttack(wolf); return; }

            Rabbit rabbit = FindNearestAnimal<Rabbit>(DetectionRadius);
            if (rabbit != null) { SetChaseOrAttack(rabbit); return; }
        }

        _state = State.Wandering;
    }

    protected override void OnKilled(ICombatant victim)
    {
        if (victim is Rabbit || victim is Wolf)
            Hunger = Mathf.Min(MaxHunger, Hunger + 50f);
    }
}
