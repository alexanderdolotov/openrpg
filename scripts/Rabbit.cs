using Godot;

// "Rabbits just run away, eat, and multiply (within reason, no
// overpopulation)." No fight instinct at all — a Rabbit never enters
// State.Attacking, even if cornered; TryAttack() is simply never
// called for this species. Eats from berry bushes (plain bushes are
// deliberately inert scenery, nothing to nibble there).
public partial class Rabbit : Animal
{
    // Population control lives here (WHEN a rabbit tries to multiply)
    // and in Main (the hard cap actually enforced, plus the separate
    // "population dropped too low, spawn more" safety net every
    // species gets) — Main asks; Main decides whether it's actually
    // allowed given the current count.
    [Signal]
    public delegate void WantsToMultiplyEventHandler();

    protected override float MoveSpeed => 90f; // quick, skittish
    protected override float DetectionRadius => 200f;
    protected override int UnarmedDamage => 0; // never attacks — see class header

    // Weak and fragile, but genuinely hard to actually hit — a real
    // (if usually moot, since it never fights back) Dexterity edge is
    // what makes "outrunning a wolf" plausible at all when Combat.
    // Resolve() ever does end up rolled against it. Left unset before
    // this — every species defaulted to Animal's own base numbers
    // (Strength/Dexterity 8, MaxHealth 30), which flattened bear/wolf/
    // rabbit into being mechanically near-identical.
    public Rabbit() { Strength = 4; Dexterity = 15; MaxHealth = 12f; }

    // Drawn, not sprited — no rabbit tile anywhere in the Kenney pack
    // this project's other world art comes from (same situation the
    // fish icon was already in — see FishIcon's own header), so this
    // matches that same "draw a simple, readable silhouette" fallback.
    private static readonly Color Body = new(0.72f, 0.62f, 0.5f);
    private static readonly Color Belly = new(0.92f, 0.88f, 0.8f);

    public override void _Draw()
    {
        DrawCircle(new Vector2(0f, 2f), 8f, Body);
        DrawCircle(new Vector2(0f, -6f), 5f, Body);
        Vector2[] earL = { new(-4f, -9f), new(-6f, -21f), new(-1f, -10f) };
        Vector2[] earR = { new(4f, -9f), new(6f, -21f), new(1f, -10f) };
        DrawColoredPolygon(earL, Body);
        DrawColoredPolygon(earR, Body);
        DrawCircle(new Vector2(0f, 6f), 3f, Belly);
        DrawCircle(new Vector2(8f, 3f), 2.5f, Belly); // tail
    }

    private const float FullEnoughToMultiply = 65f;
    private const float MultiplyCheckInterval = 20f;
    private const float MultiplyChance = 0.2f;
    private float _multiplyTimer;
    private static readonly System.Random Rng = new();

    protected override void DecideBehavior(float delta)
    {
        // A wolf nearby overrides everything else — a fleeing rabbit
        // doesn't stop to eat or think about multiplying.
        Wolf threat = FindNearestAnimal<Wolf>(DetectionRadius);
        if (threat != null)
        {
            _state = State.Fleeing;
            TargetNode = threat;
            return;
        }

        if (Hunger < MaxHunger * 0.7f)
        {
            GatherableFoliage bush = FindNearestFoliage(World.BerryBushes, DetectionRadius);
            if (bush != null)
            {
                float dist = GlobalPosition.DistanceTo(bush.GlobalPosition);
                if (dist <= EatRange)
                {
                    if (bush.AnimalEat())
                        Hunger = Mathf.Min(MaxHunger, Hunger + 25f);
                    _state = State.Wandering;
                }
                else
                {
                    TargetNode = bush;
                    _state = State.SeekingFood;
                }
                return;
            }
        }

        _state = State.Wandering;
        TryMultiply(delta);
    }

    private void TryMultiply(float delta)
    {
        _multiplyTimer += delta;
        if (_multiplyTimer < MultiplyCheckInterval) return;
        _multiplyTimer = 0f;

        if (Hunger < FullEnoughToMultiply) return;
        if (Rng.NextDouble() < MultiplyChance)
            EmitSignal(SignalName.WantsToMultiply);
    }
}
