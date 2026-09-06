using Godot;
using System.Collections.Generic;

// The tactical layer. Give it a GameAction and it handles everything
// physical about carrying it out — walking toward the target, checking
// range every physics frame, retrying the approach on its own — without
// asking the mind anything again until the action is truly resolved.
// That resolution (success or a terminal failure) is the only thing
// that goes back upstream, via ActionCompleted.
public partial class NPCActor : CharacterBody2D
{
    [Signal]
    public delegate void ActionCompletedEventHandler(Godot.Collections.Dictionary result);

    private const float Speed = 120f;
    private const float UnreachableTimeout = 10f; // seconds spent closing distance before giving up — sized for the longest realistic walk (home to the far fishing spot, ~770px), not the old tighter layout

    // protected, not private — PlayerCharacter subclasses this to add
    // free-movement input handling on top of the same state machine,
    // and needs to know when it's safe to take the wheel (Idle) versus
    // when an assigned action is already navigating/resolving.
    protected enum State { Idle, Navigating, Attempting }
    protected State _state = State.Idle;

    public GameAction CurrentAction;

    // Every item this character is carrying, of any type and count.
    // Shared, unmodified, by every subclass — this is exactly the kind
    // of thing that makes NPCActor the right base for PlayerCharacter
    // rather than a sibling reimplementation: an inventory, collision,
    // and the whole assign/navigate/attempt pipeline all come for free.
    public readonly Inventory Inventory = new();

    // Rolled fresh per character (3d6 per stat) — see CharacterStats'
    // header for why this is a separate axis from Personality. Also
    // shared for free by PlayerCharacter, same as Inventory.
    public readonly CharacterStats Stats = new();

    // Health/Fatigue — decays passively every physics frame regardless
    // of state (see the top of _PhysicsProcess below), and faster still
    // from real exertion (AppleTree/FishingSpot call Vitals.Exert()
    // directly). Shared by PlayerCharacter the same way Stats is.
    public readonly Vitals Vitals = new();

    // Auto-property, not a plain field — PlayerCharacter implements
    // IWorldCharacter, which requires CurrentEmotion as a property; a
    // bare field can't satisfy that. Behaves identically to a field for
    // every existing direct get/set call site.
    public Emotion CurrentEmotion { get; set; } = Emotion.Neutral;

    // Visual scale for the 16×16 character sprite — 2.75x lands it at
    // 44×44, the same footprint the old ColorRect placeholder used, so
    // swapping the art doesn't also silently resize anyone relative to
    // collision shapes or the camera's auto-fit framing.
    private const float SpriteScale = 2.75f;
    private AnimatedSprite2D _sprite;

    private Node2D _targetNode;
    private float _elapsed = 0f;

    // Set by AssignAction() via PathGrid for a "known" destination
    // (anything except "travel"); left null for flagpole travel, or
    // when the grid simply couldn't find a route — either way,
    // ProcessNavigating() falls back to walking straight at the target,
    // the Euclidean-heuristic case.
    private List<Vector2> _waypoints;
    private int _waypointIndex;
    private const float WaypointTolerance = 14f;

    // CharacterBody2D has always been calling MoveAndSlide() — it just
    // had nothing to slide against, since nothing here or anywhere else
    // had an actual CollisionShape2D. This is what makes NPC-vs-NPC and
    // NPC-vs-Home collision real, not just visual overlap; Godot's
    // default collision layer/mask (1/1) already matches Home's static
    // body and every other NPCActor, so no extra layer setup needed.
    public override void _Ready()
    {
        var shape = new CollisionShape2D { Shape = new CircleShape2D { Radius = 20f } };
        AddChild(shape);

        // No SpriteFrames assigned yet — SetCharacterSprite() does that,
        // called by whoever actually spawns this actor (NpcFactory for
        // an NPC, PlayerCharacter.Initialize() for itself). Creating the
        // node here regardless means both call sites just configure it,
        // never construct it.
        _sprite = new AnimatedSprite2D { Name = "Sprite", Scale = new Vector2(SpriteScale, SpriteScale) };
        _sprite.TextureFilter = TextureFilterEnum.Nearest; // keep pixel art crisp regardless of the texture's own import default
        AddChild(_sprite);
    }

    // Called once, right after construction — NpcFactory picks a variant
    // per NPC, PlayerCharacter.Initialize() picks its own. See
    // CharacterSpriteBuilder for what "variant" actually selects.
    public void SetCharacterSprite(int variantIndex)
    {
        _sprite.SpriteFrames = CharacterSpriteBuilder.Build(variantIndex);
        _sprite.Play("idle");
    }

    // Flips to face the way this character is actually moving, and
    // switches between the idle/walk animation based on whether Velocity
    // is nonzero right now — reads the same Velocity every mover here
    // already sets (ProcessNavigating's MoveAndSlide() call, or
    // PlayerCharacter's free-movement branch), so both an NPC's assigned
    // navigation and the player's WASD input animate identically without
    // either needing to know this exists. Protected, not private —
    // PlayerCharacter calls it again after its own idle-movement branch
    // sets Velocity, since that happens after this class's own
    // _PhysicsProcess (and this call within it) already ran for the
    // frame; everywhere else, this one call is all that's needed.
    protected void UpdateSpriteFacing()
    {
        if (_sprite?.SpriteFrames == null)
            return;

        if (Mathf.Abs(Velocity.X) > 1f)
            _sprite.FlipH = Velocity.X < 0f;

        string wanted = Velocity.Length() > 1f ? "walk" : "idle";
        if (_sprite.Animation != wanted)
            _sprite.Play(wanted);
    }

    public void AssignAction(GameAction action)
    {
        CurrentAction = action;
        _elapsed = 0f;

        if (action.TargetId == "")
        {
            _state = State.Attempting;
            return;
        }

        var world = GetNode<WorldRegistry>("/root/World");
        _targetNode = world.GetEntity(action.TargetId) as Node2D;
        if (_targetNode == null)
        {
            // The mind asked for something that doesn't exist anymore —
            // a tree that despawned, a stale id from an old perception.
            Finish(false, "target_not_found");
            return;
        }

        // "travel" is the flagpole case — the path is unknown by
        // design, not a grid-coverage gap, so it always walks straight
        // at the target. "follow", "trade", "steal", and "persuade" all
        // target a MOVING NPCActor (or PlayerCharacter — same class) —
        // a path computed once at the instant this action starts would
        // go stale the moment the target takes a step, so all four
        // always walk straight at wherever the target currently is too
        // (the same live GlobalPosition read every physics frame that
        // already makes following work at all). Everything else is a
        // "known," stationary destination and routes through A* when a
        // route exists; a null result (no path found) falls back to the
        // same direct movement.
        _waypoints = (action.Id == "travel" || action.Id == "follow" || action.Id == "trade" || action.Id == "steal" || action.Id == "persuade")
            ? null
            : PathGrid.FindPath(GlobalPosition, _targetNode.GlobalPosition);
        _waypointIndex = 0;

        _state = State.Navigating;
    }

    public override void _PhysicsProcess(double delta)
    {
        // Unconditional, regardless of state — this is what makes
        // fatigue decay apply the same way to the player's free-roam
        // idle movement as to an NPC's assigned actions: PlayerCharacter
        // always calls base._PhysicsProcess() first (a harmless no-op
        // here when Idle, since the switch below has no Idle case) and
        // only branches on input handling after.
        Vitals.DecayOverTime((float)delta);

        switch (_state)
        {
            case State.Navigating:
                ProcessNavigating((float)delta);
                break;
            case State.Attempting:
                ProcessAttempting();
                break;
        }

        UpdateSpriteFacing();
    }

    private void ProcessNavigating(float delta)
    {
        _elapsed += delta;

        if (_targetNode == null || !IsInstanceValid(_targetNode))
        {
            Finish(false, "target_not_found");
            return;
        }

        Vector2 finalTarget = _targetNode.GlobalPosition;
        float distToFinal = GlobalPosition.DistanceTo(finalTarget);

        // Arriving within Range always wins, regardless of whether a
        // path is still being followed — a route that happens to pass
        // close to the target shouldn't force walking all the way to
        // the last waypoint first.
        if (distToFinal <= CurrentAction.Range)
        {
            Velocity = Vector2.Zero;
            _state = State.Attempting;
            return;
        }

        if (_elapsed > UnreachableTimeout)
        {
            Velocity = Vector2.Zero;
            Finish(false, "unreachable", new Godot.Collections.Dictionary { { "distance", distToFinal } });
            return;
        }

        // Advance through A* waypoints if this action has any (a known
        // destination the grid found a route for); otherwise walk
        // straight at the target — the Euclidean-heuristic case, used
        // for flagpole travel and any known destination the grid
        // couldn't route to.
        Vector2 moveTarget = finalTarget;
        if (_waypoints != null && _waypointIndex < _waypoints.Count)
        {
            moveTarget = _waypoints[_waypointIndex];
            if (GlobalPosition.DistanceTo(moveTarget) <= WaypointTolerance)
            {
                _waypointIndex++;
                moveTarget = _waypointIndex < _waypoints.Count ? _waypoints[_waypointIndex] : finalTarget;
            }
        }

        Velocity = (moveTarget - GlobalPosition).Normalized() * Speed;
        MoveAndSlide();
    }

    private void ProcessAttempting()
    {
        // Checked before the generic "no target" branch below, since
        // sleep is also targetless but needs its own effect (restoring
        // Vitals) rather than just resolving as a no-op like wait does.
        if (CurrentAction.Id == "sleep")
        {
            Vitals.Sleep();
            Finish(true, "ok");
            return;
        }

        if (CurrentAction.TargetId == "")
        {
            Finish(true, "ok");
            return;
        }

        // "travel" has nowhere to arrive AT, exactly — a flagpole like
        // misty mountains isn't a resource to gather from, just a
        // destination. "follow" is the same shape of non-interaction:
        // the target is another NPCActor, which isn't IInteractable
        // (there's nothing to gather from a person) — arriving within
        // range IS the whole action for both, no IInteractable to ask,
        // and none is required.
        if (CurrentAction.Id == "travel" || CurrentAction.Id == "follow")
        {
            Finish(true, "arrived");
            return;
        }

        // "trade", "steal", and "persuade" all resolve directly against
        // another character rather than a world resource — neither is
        // IInteractable (there's no "gathering rules" to ask a person),
        // and the target here is literally another NPCActor (or
        // PlayerCharacter, since it IS one).
        if (CurrentAction.Id == "trade" || CurrentAction.Id == "steal" || CurrentAction.Id == "persuade")
        {
            if (_targetNode is not NPCActor targetActor || !IsInstanceValid(_targetNode))
            {
                Finish(false, "target_not_found");
                return;
            }

            if (CurrentAction.Id == "trade")
            {
                // A gift, not a negotiation — always succeeds if the
                // actor actually has what they're offering. Whether the
                // recipient WANTED it isn't checked; that's on the
                // giver's own judgment, same as speaking to someone who
                // may or may not want to hear it.
                string item = CurrentAction.Item;
                int amount = CurrentAction.Amount;
                if (!Inventory.Remove(item, amount))
                {
                    Finish(false, "insufficient_inventory");
                    return;
                }
                targetActor.Inventory.Add(item, amount);
                Finish(true, "ok", new Godot.Collections.Dictionary { { "item", item }, { "amount", amount }, { "given_to", CurrentAction.TargetId } });
                return;
            }

            if (CurrentAction.Id == "steal")
            {
                // A contested Dexterity check, not a guaranteed lift —
                // "might also fail to steal" even against a target that
                // genuinely has the item. Failing here means getting
                // caught reaching, not just guessing wrong about their
                // pockets (that failure is target_has_none, checked
                // after — no point risking getting caught over an item
                // they don't even have).
                string item = CurrentAction.Item;
                int amount = CurrentAction.Amount;
                if (!targetActor.Inventory.Has(item, amount))
                {
                    Finish(false, "target_has_none");
                    return;
                }
                var check = SkillCheck.Roll(Stats.DexterityMod, DifficultyClass.OpposedBase + targetActor.Stats.DexterityMod);
                var rollData = check.ToData("dexterity");
                rollData["item"] = item;
                if (!check.Success)
                {
                    Finish(false, "steal_failed", rollData);
                    return;
                }
                targetActor.Inventory.Remove(item, amount);
                Inventory.Add(item, amount);
                rollData["amount"] = amount;
                rollData["stolen_from"] = CurrentAction.TargetId;
                Finish(true, "ok", rollData);
                return;
            }

            // persuade: an opposed Charisma check — the actor's own
            // roll against the target's passive Charisma DC. This never
            // forces the target's next decision (see Mind's ActInstruction
            // — nothing here compels anyone); it only decides whether
            // the attempt LANDS as compelling or falls flat, which
            // NpcAgent folds into how the target hears it, exactly like
            // any other spoken line — the target's Mind still decides
            // for itself what to do about it, next turn, same as always.
            var persuadeCheck = SkillCheck.Roll(Stats.CharismaMod, DifficultyClass.OpposedBase + targetActor.Stats.CharismaMod);
            var persuadeData = persuadeCheck.ToData("charisma");
            persuadeData["target"] = CurrentAction.TargetId;
            Finish(persuadeCheck.Success, persuadeCheck.Success ? "persuasive" : "unconvincing", persuadeData);
            return;
        }

        // The target owns its own rules about whether the attempt
        // actually works right now (depleted? wrong action? hands
        // already full?) — the actor only needs to ask and relay the
        // answer. IInteractable is a compile-time contract now, not a
        // runtime HasMethod() guess.
        if (_targetNode is not IInteractable interactable || !IsInstanceValid(_targetNode))
        {
            Finish(false, "target_not_found");
            return;
        }

        InteractResult result = interactable.TryInteract(this, CurrentAction.Id);
        Finish(result.Success, result.Reason, result.Data);
    }

    private void Finish(bool success, string reason, Godot.Collections.Dictionary data = null)
    {
        var result = new Godot.Collections.Dictionary
        {
            { "action", CurrentAction.Id },
            { "target_id", CurrentAction.TargetId },
            { "message", CurrentAction.Message ?? "" }, // only meaningful for "speak"
            { "success", success },
            { "reason", reason },
            { "data", data ?? new Godot.Collections.Dictionary() },
        };
        CurrentAction = null;
        _targetNode = null;
        _waypoints = null;
        _waypointIndex = 0;
        _state = State.Idle;
        EmitSignal(SignalName.ActionCompleted, result);
    }
}
