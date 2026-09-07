using Godot;
using System.Collections.Generic;

// Wild animals — deliberately NOT LLM-driven ("more basic": wander,
// look for food, eat, sleep"). A plain finite state machine ticking
// every physics frame, the same general shape as NPCActor's own
// tactical state machine, but far lighter — no Mind, no turn loop, no
// async anything, no six-stat CharacterStats block (just the two
// numbers Combat.Resolve() actually needs). Shares ICombatant with
// NPCActor so a human's "attack" action, and an animal's own attacks,
// both go through the exact same Combat.Resolve() call.
//
// Each species (Bear/Wolf/Rabbit) overrides DecideBehavior() with its
// own rules for what to eat, what threatens it, and when to fight —
// everything else here (movement, hunger/starvation, taking damage,
// dying, wandering) is shared, not reimplemented per species.
public abstract partial class Animal : CharacterBody2D, ICombatant
{
    // Main listens on every animal it spawns to keep WorldContext.
    // Animals (and its own per-species population counts) accurate —
    // QueueFree() alone only removes the node from the scene tree, it
    // doesn't tell anything tracking population that a slot opened up.
    [Signal]
    public delegate void DiedEventHandler();

    public enum State { Wandering, SeekingFood, Eating, Fleeing, Chasing, Attacking, Resting, Dead }

    [Export] public int Strength = 8;
    [Export] public int Dexterity = 8;
    [Export] public float MaxHealth = 30f;
    public float Health;

    // 100 = full, 0 = starving. Decays passively; reaching 0 starts
    // real Health loss (StarvationDamagePerSecond) until either food is
    // found or the animal dies — "can die from starvation" is Health
    // hitting 0 this way, same mechanism a fight's damage uses.
    [Export] public float MaxHunger = 100f;
    public float Hunger;

    protected virtual float HungerDecayPerSecond => MaxHunger / 240f; // empty in ~4 real minutes of doing nothing about it
    protected virtual float StarvationDamagePerSecond => 4f;
    protected virtual float MoveSpeed => 70f;
    protected virtual float DetectionRadius => 220f; // how far this animal notices food/threats/prey at all
    protected virtual float EatRange => 40f;
    protected virtual float AttackRange => 36f;

    // How full is "not hungry" / "hungry enough to actively hunt or
    // forage" / "starving enough to attack anything" — species read
    // these at their own thresholds (a wolf's "full" cutoff isn't
    // necessarily a bear's), so these are just the shared field, not a
    // shared threshold.
    protected State _state = State.Wandering;
    public State CurrentState => _state;

    // Read-only outside view of TargetNode below — lets NpcAgent's own
    // perception tell "an animal is hostile, but toward someone else"
    // apart from "it's coming for me specifically."
    public Node2D CurrentTarget => TargetNode;

    protected WorldContext World;

    // Set by Main right after WorldRegistry.Register() — the one place
    // this animal's own id string actually lives, so anything holding
    // an Animal reference (perception text, a cached target-id list)
    // can recover its id without WorldRegistry needing a reverse
    // lookup.
    public string WorldId;
    protected Node2D TargetNode; // whatever's currently being chased/fled/attacked/eaten-from
    protected Vector2 WanderTarget;
    protected float StateTimer;

    // Set the instant ReceiveDamage() lands a hit — read by species
    // logic so "always defend if attacked" can override whatever else
    // was happening, regardless of hunger.
    protected ICombatant LastAttacker;
    protected float TimeSinceAttacked = 999f;

    private const float AttackCooldown = 1.2f; // real gap between an animal's own attack attempts — not a hit every single physics frame

    private float _attackCooldownTimer;
    private static readonly System.Random Rng = new();

    public void Initialize(WorldContext world, Vector2 position)
    {
        World = world;
        Position = position;
        Health = MaxHealth;
        Hunger = MaxHunger;
        PickNewWanderTarget();
    }

    public override void _Ready()
    {
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 14f } });
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_state == State.Dead) return;

        float dt = (float)delta;
        TimeSinceAttacked += dt;
        if (_attackCooldownTimer > 0f) _attackCooldownTimer -= dt;

        Hunger = Mathf.Max(0f, Hunger - HungerDecayPerSecond * dt);
        if (Hunger <= 0f)
            Health = Mathf.Max(0f, Health - StarvationDamagePerSecond * dt);

        if (Health <= 0f)
        {
            Die();
            return;
        }

        DecideBehavior(dt);
        Act(dt);
    }

    // Species-specific: sets _state and TargetNode based on what's
    // actually around right now (hunger level, nearby prey/threats/
    // food, whether just attacked). Movement/eating/attacking
    // themselves are all handled uniformly in Act() below, once the
    // state is decided.
    protected abstract void DecideBehavior(float delta);

    // --- shared "what's nearby" queries every species' DecideBehavior() builds on ---

    protected T FindNearestAnimal<T>(float radius) where T : Animal
    {
        T best = null;
        float bestDist = float.MaxValue;
        foreach (Animal a in World.Animals)
        {
            if (a == this || a is not T candidate || a.IsDown || !IsInstanceValid(a)) continue;
            float d = GlobalPosition.DistanceTo(a.GlobalPosition);
            if (d <= radius && d < bestDist) { bestDist = d; best = candidate; }
        }
        return best;
    }

    // Nearest human (player or NPC), as their NPCActor — what
    // ICombatant/attacking actually needs. Skips anyone already down,
    // same "can't attack what's already down" rule any Combat target
    // gets.
    protected NPCActor FindNearestHuman(float radius)
    {
        NPCActor best = null;
        float bestDist = float.MaxValue;
        foreach (IWorldCharacter c in World.Agents)
        {
            NPCActor actor = WorldContext.ActorOf(c);
            if (actor == null || actor.IsDown) continue;
            float d = GlobalPosition.DistanceTo(actor.GlobalPosition);
            if (d <= radius && d < bestDist) { bestDist = d; best = actor; }
        }
        return best;
    }

    // Nearest GatherableFoliage from a given list (berry bushes, say)
    // that still has something left on it.
    protected GatherableFoliage FindNearestFoliage(List<GatherableFoliage> source, float radius)
    {
        GatherableFoliage best = null;
        float bestDist = float.MaxValue;
        foreach (GatherableFoliage f in source)
        {
            if (!IsInstanceValid(f) || f.Count <= 0) continue;
            float d = GlobalPosition.DistanceTo(f.GlobalPosition);
            if (d <= radius && d < bestDist) { bestDist = d; best = f; }
        }
        return best;
    }

    private void Act(float delta)
    {
        switch (_state)
        {
            case State.Wandering:
                MoveToward(WanderTarget, MoveSpeed * 0.5f);
                if (GlobalPosition.DistanceTo(WanderTarget) < 20f)
                    PickNewWanderTarget();
                break;
            case State.Resting:
                Velocity = Vector2.Zero;
                MoveAndSlide();
                break;
            case State.Fleeing:
                if (TargetNode != null && IsInstanceValid(TargetNode))
                {
                    Vector2 away = (GlobalPosition - TargetNode.GlobalPosition).Normalized();
                    MoveToward(GlobalPosition + away * 100f, MoveSpeed * 1.4f);
                }
                break;
            case State.SeekingFood:
            case State.Chasing:
                if (TargetNode != null && IsInstanceValid(TargetNode))
                {
                    float dist = GlobalPosition.DistanceTo(TargetNode.GlobalPosition);
                    float stopRange = _state == State.Chasing ? AttackRange : EatRange;
                    if (dist > stopRange)
                        MoveToward(TargetNode.GlobalPosition, MoveSpeed);
                    else
                        Velocity = Vector2.Zero;
                }
                break;
            case State.Eating:
                Velocity = Vector2.Zero;
                break;
            case State.Attacking:
                TryAttack();
                break;
        }
    }

    // Shared by every predator species: close enough to already swing,
    // or still need to close the distance first.
    protected void SetChaseOrAttack(Node2D victim)
    {
        TargetNode = victim;
        _state = GlobalPosition.DistanceTo(victim.GlobalPosition) <= AttackRange ? State.Attacking : State.Chasing;
    }

    protected void MoveToward(Vector2 target, float speed)
    {
        Velocity = (target - GlobalPosition).Normalized() * speed;
        MoveAndSlide();
    }

    protected void PickNewWanderTarget()
    {
        float angle = (float)Rng.NextDouble() * Mathf.Tau;
        float dist = 60f + (float)Rng.NextDouble() * 140f;
        WanderTarget = GlobalPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
    }

    // Resolves an attack against TargetNode if in range and off
    // cooldown — shared by every species' State.Attacking, whether the
    // target is a rabbit, a human, or another animal.
    protected void TryAttack()
    {
        if (TargetNode == null || !IsInstanceValid(TargetNode) || TargetNode is not ICombatant target || target.IsDown)
        {
            _state = State.Wandering;
            return;
        }

        float dist = GlobalPosition.DistanceTo(target.GlobalPosition);
        if (dist > AttackRange)
        {
            MoveToward(target.GlobalPosition, MoveSpeed);
            return;
        }

        Velocity = Vector2.Zero;
        if (_attackCooldownTimer > 0f) return;
        _attackCooldownTimer = AttackCooldown;

        Combat.Result result = Combat.Resolve(StrengthMod, DexterityMod, target.DexterityMod, UnarmedDamage);
        if (result.Hit)
        {
            target.ReceiveDamage(result.Damage, this);
            if (target.IsDown)
                OnKilled(target);
        }
    }

    protected abstract int UnarmedDamage { get; }

    // A predator's own hunger payoff for landing the killing blow —
    // Wolf restores Hunger from a Rabbit or Bear kill, Bear from a
    // Rabbit or Wolf kill; base no-op covers Rabbit (which never
    // attacks at all) and anything killing a human (deliberately no
    // hunger reward there — an attack on a human is a threat response,
    // not treated as a meal).
    protected virtual void OnKilled(ICombatant victim) { }

    // --- ICombatant ---
    public int StrengthMod => CharacterStats.Modifier(Strength);
    public int DexterityMod => CharacterStats.Modifier(Dexterity);
    public bool IsDown => _state == State.Dead;

    public void ReceiveDamage(int amount, ICombatant attacker = null)
    {
        if (IsDown) return;
        Health = Mathf.Max(0f, Health - amount);
        TimeSinceAttacked = 0f;
        LastAttacker = attacker;
        if (Health <= 0f)
        {
            Die();
            return;
        }
        // Who hit this animal — species DecideBehavior() reads
        // LastAttacker/TimeSinceAttacked to decide whether (and how) to
        // react; nothing here forces a species-specific response.
    }

    protected virtual void Die()
    {
        _state = State.Dead;
        Velocity = Vector2.Zero;
        EmitSignal(SignalName.Died);
        QueueFree();
    }
}
