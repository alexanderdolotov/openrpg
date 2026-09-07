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

    // 100 = fully rested, 0 = exhausted — the animal equivalent of
    // Vitals.Fatigue, decaying the same "passively, all the time"
    // way Hunger already does. No Health penalty for staying awake too
    // long, unlike starvation — being tired just means it goes and
    // rests (State.Resting — see each species' own DecideBehavior,
    // right before its final "nothing better to do" wander fallback,
    // and SetResting() below) once nothing more urgent needs doing.
    [Export] public float MaxFatigue = 100f;
    public float Fatigue;

    protected virtual float HungerDecayPerSecond => MaxHunger / 240f; // empty in ~4 real minutes of doing nothing about it
    protected virtual float StarvationDamagePerSecond => 4f;
    protected virtual float FatigueDecayPerSecond => MaxFatigue / 300f; // empty in ~5 real minutes — a little slower to drain than Hunger, faster than a human's own Fatigue
    protected virtual float FatigueRecoveryPerSecond => MaxFatigue / 15f; // a nap, not an all-night sleep — full recovery in ~15 real seconds of actually resting
    protected virtual float RestThreshold => 30f; // below this, tired enough to actually go rest rather than keep wandering/foraging
    public bool NeedsRest => Fatigue < RestThreshold;

    // "Animals need to sleep for a period of time to restore fatigue,
    // unless interrupted by being attacked" — a real, reported bug
    // without this: DecideBehavior re-evaluates completely fresh every
    // single physics tick regardless of current state, so literally
    // anything DecideBehavior finds more interesting than resting (a
    // rabbit wandering within a hungry wolf's own DetectionRadius,
    // Hunger drifting back above/below FullThreshold mid-rest) would
    // immediately abandon the rest the very next tick — visibly
    // flickering between resting and moving instead of ever actually
    // completing one. Once actually resting, this stays true (and
    // species' own DecideBehavior checks it FIRST, right after their
    // own "defend if attacked" check, returning early without
    // reconsidering anything else) until Fatigue is fully restored —
    // Fatigue only ever goes UP while resting (Act()'s own Resting
    // case) and only down otherwise, so this alone is enough to commit
    // to "keep resting" without a separate timer to track and rewind
    // if the rest gets legitimately interrupted.
    public bool IsCommittedToResting => _state == State.Resting && Fatigue < MaxFatigue;
    protected virtual float MoveSpeed => 70f;
    protected virtual float DetectionRadius => 220f; // how far this animal notices food/threats/prey at all
    protected virtual float EatRange => 40f;
    protected virtual float AttackRange => 36f;

    // How much faster than MoveSpeed a fleeing animal moves — species
    // override this, not the fixed 1.4x every species used to share
    // regardless of who or what they were fleeing (see Rabbit's own
    // override for "run away even faster" right after actually being
    // attacked).
    protected virtual float FleeSpeedMultiplier => 1.4f;

    // The speed used while actually closing distance on a target
    // (Chasing/Attacking's own approach, and SeekingFood's walk to a
    // foliage target) — plain MoveSpeed by default. Wolf overrides
    // this to move much slower while stalking a rabbit that hasn't
    // noticed it yet: a fleeing rabbit is faster than a wolf at full
    // chase speed (confirmed — a wolf genuinely cannot run one down
    // once it's actually running), so the only way a wolf ever catches
    // one is by closing the gap BEFORE it's spotted, not by outrunning
    // it after.
    protected virtual float ChaseSpeedFor(Node2D target) => MoveSpeed;

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

    // Main.Log, same callback NpcAgent uses for its own console lines
    // — "why animals do what they do" wasn't visible ANYWHERE before
    // this (Animal/Wolf/Bear/Rabbit never logged a thing), which made
    // troubleshooting a wolf's behavior impossible short of reading
    // the code. Set by Initialize(), null-checked everywhere it's used
    // since a couple of headless test/diagnostic paths construct an
    // Animal without one. ThoughtLog is the same opt-in persistent
    // file NpcAgent's own thoughts/actions already go to (see
    // NpcThoughtLogger) — the console scrolls and can be missed live;
    // this is what's actually searchable afterward.
    protected System.Action<string, string> UiLog;
    protected NpcThoughtLogger ThoughtLog;

    // The one place every logged animal event actually goes through —
    // both UiLog (the live console) and ThoughtLog (the persistent
    // file), from a single call site and a single message, rather
    // than every log call site in this file (and Wolf/Bear/Rabbit)
    // building and passing the same line to two different loggers
    // separately.
    protected void LogEvent(string message, string colorHex)
    {
        UiLog?.Invoke($"[{DescribeSelf()}] {message}", colorHex);
        ThoughtLog?.Log(DescribeSelf(), "ANIMAL", message);
    }

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

    public void Initialize(WorldContext world, Vector2 position, System.Action<string, string> uiLog = null, NpcThoughtLogger thoughtLog = null)
    {
        World = world;
        UiLog = uiLog;
        ThoughtLog = thoughtLog;
        Position = position;
        Health = MaxHealth;
        Hunger = MaxHunger;
        Fatigue = MaxFatigue;
        PickNewWanderTarget();
    }

    // Same floating readout NPCActor gives every NPC — see
    // VitalsBarDisplay's own header. All three bars now that animals
    // have their own Fatigue too (see Refresh()'s call site below).
    private VitalsBarDisplay _vitalsBars;

    // "Animals should show a ZZZ sign if sleeping" — the same 💤
    // NPCActor already flashes above a sleeping human (see its own
    // BeginSleepVisual), just simpler: no emotion system to borrow a
    // slot from here, so this is its own small Label, toggled purely
    // by _state every physics tick (see _PhysicsProcess below) rather
    // than needing a dedicated show/hide call at every single place
    // State.Resting could start or end.
    private Label _sleepLabel;

    public override void _Ready()
    {
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 14f } });
        _vitalsBars = new VitalsBarDisplay { Position = new Vector2(0f, -34f) };
        AddChild(_vitalsBars);

        _sleepLabel = new Label { Position = new Vector2(-10f, -52f), Visible = false };
        _sleepLabel.AddThemeFontSizeOverride("font_size", 18);
        _sleepLabel.Text = "💤";
        AddChild(_sleepLabel);
    }

    // The attack "animation" — see CombatVisuals.PlayLunge's own
    // header for why this exists at all (no attack-frame art
    // anywhere). Animal draws itself directly (no separate sprite
    // child with its own Tween-able Position the way NPCActor has —
    // see CombatVisuals' own comment on that), so this is a plain
    // manually-driven offset instead: a species' own _Draw() applies
    // it via DrawSetTransform, same mechanism Rabbit's hop already
    // uses for itself. _Process (not _PhysicsProcess) since, like the
    // hop, this is purely cosmetic.
    private Vector2 _lungeOffset = Vector2.Zero;
    private Vector2 _lungeDirection = Vector2.Zero;
    private float _lungeTimer = 0f;
    private const float LungeOutTime = 0.12f;
    private const float LungeBackTime = 0.18f;

    protected void PlayLunge(Vector2 towardWorldPosition)
    {
        Vector2 direction = towardWorldPosition - GlobalPosition;
        if (direction.LengthSquared() < 1f) return;
        _lungeDirection = direction.Normalized();
        _lungeTimer = LungeOutTime + LungeBackTime;
    }

    // Exposed read-only for a species' own _Draw() (or _Process, for
    // one that combines this with its own offset, like Rabbit's hop)
    // to actually apply.
    protected Vector2 LungeOffset => _lungeOffset;

    public override void _Process(double delta)
    {
        if (_lungeTimer <= 0f) return;

        _lungeTimer -= (float)delta;
        float total = LungeOutTime + LungeBackTime;
        float elapsed = Mathf.Max(0f, total - _lungeTimer);
        float t = elapsed < LungeOutTime
            ? elapsed / LungeOutTime
            : 1f - (elapsed - LungeOutTime) / LungeBackTime;
        _lungeOffset = _lungeDirection * 8f * Mathf.Clamp(t, 0f, 1f);
        QueueRedraw();

        if (_lungeTimer <= 0f)
        {
            _lungeOffset = Vector2.Zero;
            QueueRedraw();
        }
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
        // Passive decay always applies, same as Hunger — actual
        // recovery only happens while State.Resting (Act()'s own
        // Resting case), so this alone is a one-way drain toward
        // "goes to rest eventually," not a wash.
        Fatigue = Mathf.Max(0f, Fatigue - FatigueDecayPerSecond * dt);
        _vitalsBars?.Refresh(Health / MaxHealth, Fatigue / MaxFatigue, Hunger / MaxHunger);
        if (_sleepLabel != null)
            _sleepLabel.Visible = _state == State.Resting;

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
                Fatigue = Mathf.Min(MaxFatigue, Fatigue + FatigueRecoveryPerSecond * delta);
                break;
            case State.Fleeing:
                if (TargetNode != null && IsInstanceValid(TargetNode))
                {
                    Vector2 away = (GlobalPosition - TargetNode.GlobalPosition).Normalized();
                    MoveToward(GlobalPosition + away * 100f, MoveSpeed * FleeSpeedMultiplier);
                }
                break;
            case State.SeekingFood:
            case State.Chasing:
                if (TargetNode != null && IsInstanceValid(TargetNode))
                {
                    float dist = GlobalPosition.DistanceTo(TargetNode.GlobalPosition);
                    float stopRange = _state == State.Chasing ? AttackRange : EatRange;
                    if (dist > stopRange)
                        MoveToward(TargetNode.GlobalPosition, ChaseSpeedFor(TargetNode));
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

    // "Before engaging combat, a creature should size up their
    // opponent to see if they have a real chance." A rough gut-check,
    // not a fair fight calculation — an animal can't see an opponent's
    // exact numbers from the outside, only get a sense of how tough
    // they look, which StrengthMod (shared by every ICombatant, human
    // or animal alike) is a fine stand-in for. Doesn't need to expect
    // an outright win, just not be hopelessly overmatched. Species
    // call this from their own starving/desperate branches — never
    // from "defend if attacked" (once something's already come at you,
    // sizing it up first isn't really an option anymore) — and skip it
    // entirely once truly desperate (see each species' own
    // DesperateThreshold): "wolves become very aggressive: will fight
    // even stronger animals than them" once hunger is bad enough.
    protected bool WinnableFight(ICombatant opponent) => StrengthMod + 2 >= opponent.StrengthMod;

    // "<Wolf> (animal_4)" — how this animal names ITSELF in a log
    // line. GetType().Name is "Wolf"/"Bear"/"Rabbit" directly, no
    // per-species override needed.
    protected string DescribeSelf() => $"{GetType().Name} ({WorldId})";

    // How this animal names WHATEVER it's going after, in a log line —
    // a person by their actual name (via World.NameOf, the same
    // reverse lookup NpcAgent's own perception uses), another animal
    // by species+id, or a bare fallback if it's something unexpected.
    protected string DescribeTarget(Node2D t)
    {
        if (t is NPCActor actor)
            return World?.NameOf(actor) ?? "someone";
        if (t is Animal a)
            return $"a {a.GetType().Name.ToLower()} ({a.WorldId})";
        return "something";
    }

    // Shared by every predator species: close enough to already swing,
    // or still need to close the distance first. Only logs on an
    // actual NEW engagement (a different target than whatever it was
    // already going after) — DecideBehavior calls this again every
    // single physics tick for as long as a chase continues, so
    // logging unconditionally here would spam the console at 60/sec
    // instead of reading as one real event.
    protected void SetChaseOrAttack(Node2D victim, string reason = null)
    {
        if (!ReferenceEquals(TargetNode, victim))
        {
            string reasonNote = reason != null ? $" ({reason})" : "";
            LogEvent($"goes after {DescribeTarget(victim)}{reasonNote}", "d8a97a");
        }
        TargetNode = victim;
        _state = GlobalPosition.DistanceTo(victim.GlobalPosition) <= AttackRange ? State.Attacking : State.Chasing;
    }

    // The flee equivalent of SetChaseOrAttack above — same "only log a
    // genuinely new reaction" treatment, used by any species that
    // flees (currently just Rabbit, but shared here rather than
    // reimplemented per species that gets a flee instinct later).
    protected void SetFleeing(Node2D from, string reason = null)
    {
        if (_state != State.Fleeing || !ReferenceEquals(TargetNode, from))
        {
            string reasonNote = reason != null ? $" ({reason})" : "";
            LogEvent($"flees from {DescribeTarget(from)}{reasonNote}", "d8a97a");
        }
        TargetNode = from;
        _state = State.Fleeing;
    }

    // Species call this from their own DecideBehavior once NeedsRest
    // is true and nothing more urgent needs doing (right before the
    // final wander fallback — see each species' own DecideBehavior).
    // Waking back up needs no separate method: DecideBehavior
    // re-evaluates every physics tick regardless of current state
    // (same reason a resting animal still wakes up and defends itself
    // immediately if attacked — see each species' own "defend" check,
    // always first), so once NeedsRest goes false again (Fatigue
    // recovered — see Act()'s own Resting case) the next tick's normal
    // priority chain just picks something else, with nothing here
    // needing to notice or announce the transition itself.
    // Deliberately silent, unlike SetChaseOrAttack/SetFleeing above —
    // "going to rest" is routine, happens constantly across however
    // many animals are alive, and isn't the kind of thing worth
    // troubleshooting the way an attack or a chase is; logging it just
    // buried the log lines that actually explain something in noise.
    // The 💤 above a resting animal's head (see _sleepLabel) still
    // shows it visually — this only trims the console/thought-log line.
    protected void SetResting()
    {
        _state = State.Resting;
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
            MoveToward(target.GlobalPosition, ChaseSpeedFor(TargetNode));
            return;
        }

        Velocity = Vector2.Zero;
        if (_attackCooldownTimer > 0f) return;
        _attackCooldownTimer = AttackCooldown;

        Combat.Result result = Combat.Resolve(StrengthMod, DexterityMod, target.DexterityMod, UnarmedDamage);

        // Same visual treatment a human's own attack gets (see
        // NPCActor.ProcessAttempting's "attack" case) — a lunge toward
        // whoever's being hit, and a floating number or "miss" above
        // them, so an animal's attack reads as something actually
        // happening on screen too, not just numbers changing silently.
        PlayLunge(target.GlobalPosition);
        if (GetParent() is Node parent)
        {
            if (result.Hit) CombatVisuals.ShowDamage(parent, target.GlobalPosition, result.Damage);
            else CombatVisuals.ShowMiss(parent, target.GlobalPosition);
        }

        if (result.Hit)
        {
            string verb = result.Damage >= MaxHealth * 0.3f ? "mauls" : "bites"; // MaxHealth here is THIS animal's own — just a rough "hit hard vs a graze" split, not a real threshold about the victim
            LogEvent($"{verb} {DescribeTarget(target as Node2D)} for {result.Damage}", "e0876b");
            target.ReceiveDamage(result.Damage, this);
            if (target.IsDown)
            {
                LogEvent($"brings down {DescribeTarget(target as Node2D)}", "e0876b");
                OnKilled(target);
            }
        }
        else
        {
            LogEvent($"lunges at {DescribeTarget(target as Node2D)} and misses", "8a8f86");
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
            Die(fromCombat: true);
            return;
        }
        // Who hit this animal — species DecideBehavior() reads
        // LastAttacker/TimeSinceAttacked to decide whether (and how) to
        // react; nothing here forces a species-specific response.
    }

    // fromCombat: true only from ReceiveDamage above — _PhysicsProcess's
    // own call (the other, default caller) only ever fires from the
    // starvation branch (Health <= 0f there is never reached from a
    // hit — ReceiveDamage already calls Die() and returns the instant
    // that happens, and _state == State.Dead short-circuits every
    // later _PhysicsProcess call for this animal before it can
    // re-check Health at all). Without this flag, a starving animal
    // that ALSO happens to take a killing blow in combat — Hunger
    // already at 0 either way — would get logged as "starved to
    // death" even though the actual cause was the hit, not the
    // hunger; fromCombat is what keeps the two apart.
    protected virtual void Die(bool fromCombat = false)
    {
        string cause = !fromCombat && Hunger <= 0f ? "starved to death" : "died";
        LogEvent(cause, "6b4a4a");
        _state = State.Dead;
        Velocity = Vector2.Zero;
        EmitSignal(SignalName.Died);
        QueueFree();
    }
}
