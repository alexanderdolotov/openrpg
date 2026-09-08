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

    protected override float MoveSpeed => 95f; // quick, skittish — a touch faster than the original 90, not a sprint
    protected override float DetectionRadius => 200f;
    protected override int UnarmedDamage => 0; // never attacks — see class header

    // "Run away even faster" once actually attacked (by anything —
    // human or wolf), on top of the ordinary wolf-sighted flee speed.
    // Toned down from an earlier pass that had these way too fast
    // (2.4x/1.7x — a rabbit was outrunning everything on screen by a
    // silly margin). Base Animal's FleeSpeedMultiplier (1.4x) is what
    // a wolf itself uses for comparison; a rabbit still edges that out
    // enough to plausibly outrun a chasing wolf (95×1.45 ≈ 138 vs a
    // wolf's own 110 chase speed), with a modest, short-lived kick
    // right after actually being hit, not a permanent second gear.
    protected override float FleeSpeedMultiplier => TimeSinceAttacked < RecentAttackWindow ? 1.7f : 1.45f;

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

    // "Should hop" — a small vertical bounce while actually moving,
    // settling flat the instant it stops. _Draw() itself stays a plain
    // static silhouette (same shapes as before); this only ever
    // nudges the whole drawing up and down via DrawSetTransform, not a
    // redesign of the art. _Process (not _PhysicsProcess) since this
    // is purely cosmetic — no reason to tie a hop's timing to the
    // physics step.
    private float _hopPhase;
    private const float HopHeight = 5f;
    private const float HopRate = 11f; // radians/sec — a quick bounce, not a slow bob

    public override void _Process(double delta)
    {
        base._Process(delta); // Animal's own lunge-offset upkeep — see its header comment

        bool moving = Velocity.Length() > 1f;
        if (moving)
        {
            _hopPhase += (float)delta * HopRate;
            QueueRedraw();
        }
        else if (_hopPhase != 0f)
        {
            _hopPhase = 0f; // settle flat the instant it stops, not mid-bounce
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        // Only ever airborne on the upswing of the sine wave — a
        // resting/settling rabbit's feet stay on the ground, never sink
        // below it. Combined with LungeOffset since a rabbit never
        // actually lunges at anything itself (see class header — no
        // fight instinct at all) — this is here only for symmetry/
        // future-proofing, effectively always zero in practice.
        float hop = _hopPhase != 0f ? -Mathf.Abs(Mathf.Sin(_hopPhase)) * HopHeight : 0f;
        DrawSetTransform(new Vector2(0f, hop) + LungeOffset);

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

    // How long "just got attacked" keeps overriding everything else —
    // see FleeSpeedMultiplier above and DecideBehavior below. Short on
    // purpose: "forgetful," so a rabbit that gets away is genuinely
    // catchable again soon after, not permanently spooked for the rest
    // of its life.
    private const float RecentAttackWindow = 6f;

    // "A wolf would never catch a bunny at their relative speeds — there
    // needs to be a stealth check that a bunny can fail to notice the
    // wolf and get caught sometimes." Confirmed true once Bravery/speed
    // tuning landed: a fleeing rabbit (95×1.45 ≈ 138) genuinely
    // outruns a wolf's own chase speed (110) — no amount of numbers
    // tweaking fixes that without making one side silly, so the fix is
    // earlier in the timeline instead: a wolf can close the gap
    // BEFORE the rabbit ever starts running (see Wolf.ChaseSpeedFor's
    // own "stalking" slowdown), for as long as this keeps failing to
    // notice it. Once noticed, this stays true (a rabbit doesn't
    // un-notice a wolf still standing right there) until no wolf is in
    // range at ALL — reset in the else branch below — so a wolf that
    // breaks off and a DIFFERENT (or the same, later) one that shows
    // up again both get a fresh, genuine chance to sneak up unnoticed.
    private bool _noticedThreat;
    private float _noticeCheckTimer;
    private const float NoticeCheckInterval = 0.75f; // real seconds between rolls — not every physics tick, see NoticesWolf's own header

    // A periodic opposed Dexterity check (same SkillCheck/DifficultyClass
    // shape every other roll in this game uses), not an instant, certain
    // alert the moment a wolf enters detection range — closer is
    // genuinely easier to notice (louder, closer, harder to miss) than
    // right at the edge of DetectionRadius, so the DC scales with
    // distance: roughly DC 11 right on top of you, DC 17 at the far
    // edge of range.
    private bool NoticesWolf(Wolf wolf, float delta)
    {
        if (_noticedThreat) return true;

        _noticeCheckTimer += delta;
        if (_noticeCheckTimer < NoticeCheckInterval) return false;
        _noticeCheckTimer = 0f;

        float distFrac = Mathf.Clamp(GlobalPosition.DistanceTo(wolf.GlobalPosition) / DetectionRadius, 0f, 1f);
        int dc = DifficultyClass.OpposedBase + wolf.DexterityMod + (int)(distFrac * 6f);
        var check = SkillCheck.Roll(DexterityMod, dc);
        if (check.Success)
        {
            _noticedThreat = true;
            // Level 2 only — same "always animal-vs-animal, never a
            // real threat to a human" reasoning as SetFleeing's own
            // gate; see GameSettings.LogLevel.
            if (GameSettings.LogLevel >= 2)
                LogEvent($"notices {DescribeTarget(wolf)} closing in! [{check.Describe()}]", "e0c66a");
        }
        return _noticedThreat;
    }

    protected override void DecideBehavior(float delta)
    {
        // Just got hit — by a human or a wolf, doesn't matter which —
        // the highest-priority, extra-fast flee burst. Whoever actually
        // landed the hit is who it runs from specifically, not just
        // "away from wherever it was standing."
        if (TimeSinceAttacked < RecentAttackWindow && LastAttacker is Node2D attacker && IsInstanceValid(attacker))
        {
            _noticedThreat = true; // a hit is impossible to miss, whatever the notice roll would've said
            SetFleeing(attacker, "just attacked");
            return;
        }

        // A wolf nearby overrides everything else once actually
        // noticed — a fleeing rabbit doesn't stop to eat or think
        // about multiplying. One that HASN'T noticed yet, though,
        // just keeps doing whatever it was already doing below (eat,
        // wander, multiply) while the wolf may be quietly closing in —
        // see NoticesWolf's own header.
        Wolf threat = FindNearestAnimal<Wolf>(DetectionRadius);
        if (threat != null)
        {
            if (NoticesWolf(threat, delta))
            {
                SetFleeing(threat);
                return;
            }
        }
        else
        {
            _noticedThreat = false; // nothing in range at all — a fresh sighting later starts from a clean slate
        }

        // Already mid-rest and not fully recovered yet — keep resting.
        // Checked AFTER the two flee checks above (a genuine threat —
        // an actual hit, or a spotted wolf — still needs to interrupt
        // a resting rabbit; that's real self-preservation, not the
        // routine "something slightly more interesting came along"
        // churn IsCommittedToResting exists to block — see its own
        // header) but before hunger/wandering, which otherwise
        // flickered a resting rabbit in and out of State.Resting every
        // time its own Hunger crossed back and forth over 70%.
        if (IsCommittedToResting) return;

        if (Hunger < MaxHunger * 0.7f)
        {
            // Berries first (more filling, see the point-value split
            // below) — grass only considered when there's no bush
            // within range at all. "Rabbits starving instead of finding
            // berries or grass to eat" — a handful of bushes shared
            // across the whole map (and every animal on it) meant an
            // unlucky rabbit could easily have NOTHING within
            // DetectionRadius; grass patches are deliberately common
            // (see Main.BuildWorld/GenerateGrassPatch) specifically as
            // a fallback that's far more likely to actually be nearby.
            GatherableFoliage bush = FindNearestFoliage(World.BerryBushes, DetectionRadius);
            GatherableFoliage food = bush ?? FindNearestFoliage(World.GrassPatches, DetectionRadius);
            if (food != null)
            {
                float dist = GlobalPosition.DistanceTo(food.GlobalPosition);
                if (dist <= EatRange)
                {
                    if (food.AnimalEat())
                        Hunger = Mathf.Min(MaxHunger, Hunger + (food == bush ? 25f : 12f));
                    _state = State.Wandering;
                }
                else
                {
                    TargetNode = food;
                    _state = State.SeekingFood;
                }
                return;
            }
        }

        // Nothing more urgent to do — rest instead of just wandering
        // aimlessly if it's actually tired.
        if (NeedsRest)
        {
            SetResting();
            return;
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

    // "When they die they should drop fur and rabbit meat into the
    // inventory" — whoever's inventory, specifically: LastAttacker, the
    // one who actually landed the killing blow, same field Wolf/Bear
    // already read for their own "defend if attacked" logic. Only
    // humans have an Inventory at all (a wolf or bear killing a rabbit
    // already gets its own reward — see Wolf/Bear's OnKilled — a real
    // pelt/meat drop on top of that would be double-dipping for them,
    // and there's nowhere to put it anyway). Neither item does
    // anything yet — meat isn't edible until a future cooking system,
    // fur has no use yet either (Food.IsFood() doesn't know either
    // name, same as a pinecone) — this only ever adds them to
    // Inventory, nothing here or elsewhere consumes them.
    protected override void Die(bool fromCombat = false)
    {
        if (LastAttacker is NPCActor killer)
        {
            killer.Inventory.Add("rabbit_meat", 1);
            killer.Inventory.Add("fur", 1);
        }
        base.Die(fromCombat);
    }
}
