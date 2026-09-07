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
    // Sleeping is its own state, not just a fast path through Attempting
    // — it needs to actually occupy real time (SleepDuration) rather
    // than resolve instantly, and PlayerCharacter's free-movement branch
    // only runs when _state == Idle, so giving sleep a distinct non-Idle
    // state is what makes a sleeping player immobile for free, with no
    // separate "don't let WASD move you right now" check needed.
    protected enum State { Idle, Navigating, Attempting, Sleeping }
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

    // A real property with a backing field, not an auto-property — the
    // setter also flashes an emoji above this character's head, but only
    // on an actual CHANGE of mood, not every reassignment (NpcAgent sets
    // this every single turn, same value or not) — every existing call
    // site that already does `actor.CurrentEmotion = ...` gets the
    // visual for free, with no separate "and now update the display"
    // step anyone has to remember.
    private Emotion _currentEmotion = Emotion.Neutral;
    public Emotion CurrentEmotion
    {
        get => _currentEmotion;
        set
        {
            bool changed = value != _currentEmotion;
            _currentEmotion = value;
            if (changed)
                FlashEmotionEmoji();
        }
    }

    // Visual scale for the 16×16 character sprite — 2.75x lands it at
    // 44×44, the same footprint the old ColorRect placeholder used, so
    // swapping the art doesn't also silently resize anyone relative to
    // collision shapes or the camera's auto-fit framing.
    private const float SpriteScale = 2.75f;
    private AnimatedSprite2D _sprite;

    // Above-the-head UI — world-space children (not CanvasLayer), so
    // they move and zoom with the character exactly like the sprite
    // does, no per-frame screen-position projection needed. The emoji
    // sits just above the sprite; the speech bubble sits above that so
    // the two never overlap when both are showing. Positions are tuned
    // against the sprite's feet-anchored top edge (SpriteTopY below),
    // not the old center-anchored one — the sprite got ~22px taller
    // above the origin when it moved to standing on its own feet
    // (matching AppleTree's fix for the same "Y-sort should compare
    // ground-contact points, not centers" reasoning), so anything meant
    // to sit "just above the head" had to move up by the same amount or
    // it'd end up nearly touching the top of the head instead.
    private const float SpriteTopY = -SpriteScale * CharacterSpriteBuilder.FrameSize;
    private Label _emotionLabel;
    private ulong _emotionToken; // same late-timer guard as _speechToken, see FlashEmotionEmoji()
    private PanelContainer _speechBubble;
    private ulong _speechToken; // guards against a late timer hiding a NEWER bubble than the one it was scheduled for

    private Node2D _targetNode;
    private float _elapsed = 0f;

    // How long State.Attempting sits there before actually resolving —
    // reaching for an apple, casting a line, reaching into someone's
    // pocket all take a beat, not an instant frame the moment you're in
    // range. A SEPARATE counter from _elapsed on purpose: _elapsed is
    // already mid-count by the time Attempting starts for anything that
    // needed to walk there first (it's tracking UnreachableTimeout
    // during Navigating), so reusing it here would let the attempt
    // resolve on literally the first frame for every action that
    // required travel — defeating the whole point. Reset at both
    // Attempting entry points (AssignAction's direct branch, and
    // ProcessNavigating's arrival transition).
    private const float AttemptDuration = 1.5f;
    private float _attemptElapsed = 0f;

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
        // Not centered — anchored bottom-center (feet at this node's own
        // origin), same reasoning and same math as AppleTree's sprite
        // anchor: Y-sort compares each node's own Position, so it needs
        // to actually correspond to the visual "standing on the ground"
        // point for depth to read correctly, not an arbitrary sprite
        // center floating around the character's torso.
        _sprite = new AnimatedSprite2D
        {
            Name = "Sprite",
            Scale = new Vector2(SpriteScale, SpriteScale),
            Centered = false,
            Offset = new Vector2(-CharacterSpriteBuilder.FrameSize / 2f, -CharacterSpriteBuilder.FrameSize),
        };
        _sprite.TextureFilter = TextureFilterEnum.Nearest; // keep pixel art crisp regardless of the texture's own import default
        AddChild(_sprite);

        BuildEmotionLabel();
        BuildSpeechBubble();
        // Deliberately no initial flash here — showing Neutral the
        // moment a character spawns isn't a "mood change," it's just
        // the starting state; the emoji only appears once CurrentEmotion
        // actually changes to something.
    }

    private void BuildEmotionLabel()
    {
        _emotionLabel = new Label { Name = "EmotionLabel", Position = new Vector2(-14, SpriteTopY - 24f), Visible = false };
        _emotionLabel.AddThemeFontSizeOverride("font_size", 22);
        AddChild(_emotionLabel);
    }

    // A plain "talking..." indicator, not the actual words — the real
    // line is already in the console log (and, for an NPC, in Memory);
    // showing the full text above their head too just meant a variable-
    // length box competing with everything else on screen. This is
    // fixed-size and fixed-content on purpose: nothing here needs to
    // resize to fit varying text, so there's no auto-sizing/layout-
    // timing risk to get wrong blind.
    private void BuildSpeechBubble()
    {
        _speechBubble = new PanelContainer
        {
            Name = "SpeechBubble",
            Position = new Vector2(-45, SpriteTopY - 78f),
            CustomMinimumSize = new Vector2(90, 0),
            Visible = false,
        };
        _speechBubble.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(1f, 1f, 1f, 0.95f), // white cloud, not the old dark box
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4,
        });

        var label = new Label { Text = "talking...", HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeColorOverride("font_color", new Color(0.15f, 0.15f, 0.18f)); // dark text on the white bubble
        label.AddThemeFontSizeOverride("font_size", 13);
        _speechBubble.AddChild(label);

        AddChild(_speechBubble);
    }

    // Shows briefly on an actual mood CHANGE, not every turn — see
    // CurrentEmotion's setter, the only caller. Same late-timer guard as
    // ShowSpeechBubble() below: a token, not a flag, so a slow-to-fire
    // hide from an OLDER flash can't hide a NEWER one that's already
    // showing a different emoji.
    private void FlashEmotionEmoji()
    {
        if (_emotionLabel == null) return;
        _emotionLabel.Text = _currentEmotion.ToEmoji();
        _emotionLabel.Visible = true;

        // processAlways:false — while the game's paused, everything
        // about a character should be frozen, including how long a
        // flashed emoji stays up.
        ulong myToken = ++_emotionToken;
        GetTree().CreateTimer(2f, processAlways: false).Timeout += () =>
        {
            if (myToken == _emotionToken && IsInstanceValid(_emotionLabel))
                _emotionLabel.Visible = false;
        };
    }

    // Called whenever this character actually says something out loud —
    // speak uses it (see NpcAgent.OnActionCompleted and PlayerCharacter's
    // chat handling), right alongside the existing SpeechLog.Say() call.
    // Purely a "they're talking right now" indicator — SpeechLog (and
    // the console log) still carries the actual words; this never did
    // and now doesn't try to.
    public void ShowSpeechBubble(float duration = 2.5f)
    {
        if (_speechBubble == null) return;
        _speechBubble.Visible = true;

        ulong myToken = ++_speechToken;
        GetTree().CreateTimer(duration, processAlways: false).Timeout += () =>
        {
            // Only hide if nothing newer has shown since — otherwise a
            // slow-to-fire timer from an OLDER line could hide a bubble
            // that's already showing for a more recent one.
            if (myToken == _speechToken && IsInstanceValid(_speechBubble))
                _speechBubble.Visible = false;
        };
    }

    // The one authoritative "is sleep currently a real option" rule —
    // called from Mind's tool-offering/validation for NPCs (via
    // NpcAgent) and from the action panel's button visibility for the
    // player, so the rule only lives in one place rather than risking
    // the two drifting apart. Three tiers: too well-rested to bother
    // (never offered, anywhere); truly exhausted (offered anywhere —
    // being desperate doesn't wait for a walk home); everything between
    // (offered only near home — tired enough to consider it, not
    // desperate enough to just lie down in a field).
    public bool CanSleep(Vector2 homePosition)
    {
        if (Vitals.Fatigue > Vitals.SleepUnnecessaryThreshold)
            return false;
        if (Vitals.NeedsSleep)
            return true;
        return GlobalPosition.DistanceTo(homePosition) <= ActionRanges.SleepNearHome;
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

        // Checked before the generic targetless branch below — sleep is
        // ALSO targetless, but needs its own dedicated state (real
        // duration, immobility) rather than the instant-resolve those
        // other targetless actions (wait) get.
        if (action.Id == "sleep")
        {
            _state = State.Sleeping;
            BeginSleepVisual();
            return;
        }

        if (action.TargetId == "")
        {
            _state = State.Attempting;
            _attemptElapsed = 0f;
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
        // at the target. "follow", "trade", and "steal" all target a
        // MOVING NPCActor (or PlayerCharacter — same class) — a path
        // computed once at the instant this action starts would go
        // stale the moment the target takes a step, so all three always
        // walk straight at wherever the target currently is too (the
        // same live GlobalPosition read every physics frame that
        // already makes following work at all). Everything else is a
        // "known," stationary destination and routes through A* when a
        // route exists; a null result (no path found) falls back to the
        // same direct movement.
        _waypoints = (action.Id == "travel" || action.Id == "follow" || action.Id == "trade" || action.Id == "steal")
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
                ProcessAttempting((float)delta);
                break;
            case State.Sleeping:
                ProcessSleeping((float)delta);
                break;
        }

        // Sleeping drives its own visual (rotated sprite, no walk/idle
        // switching) via BeginSleepVisual()/EndSleepVisual() — skip the
        // normal facing/animation logic entirely while asleep instead of
        // fighting it every frame.
        if (_state != State.Sleeping)
            UpdateSpriteFacing();
    }

    private const float SleepDuration = 20f;

    private void ProcessSleeping(float delta)
    {
        // No Velocity change, no MoveAndSlide() call — simply not moving
        // this body is what "immobile" means for a CharacterBody2D;
        // there's nothing else that could move it out from under this.
        _elapsed += delta;
        if (_elapsed < SleepDuration)
            return;

        Vitals.Sleep();
        EndSleepVisual();
        Finish(true, "ok");
    }

    // A cheap way to read as "lying down" without new art — the
    // existing character sheet has no sleeping pose to switch to, so
    // this rotates the whole sprite on its side instead. Zzz is a
    // separate, deliberately reused slot: it borrows the emotion
    // emoji's position/label rather than adding a third above-the-head
    // element, since the two never need to show at once anyway.
    private void BeginSleepVisual()
    {
        _sprite.RotationDegrees = 90f;
        if (_emotionLabel != null)
        {
            // Bumping the token (not just setting Text/Visible) matters:
            // NpcAgent sets CurrentEmotion right before AssignAction(),
            // so a turn that BOTH changes mood AND chooses sleep starts
            // a 2-second FlashEmotionEmoji hide-timer moments before this
            // runs. Without invalidating it, that timer fires ~2s later
            // and hides Zzz for the remaining ~18s of a 20s sleep — its
            // token check has no idea sleep started and would just see
            // "nothing newer has shown since," which used to be true.
            _emotionToken++;
            _emotionLabel.Text = "💤";
            _emotionLabel.Visible = true;
        }
    }

    private void EndSleepVisual()
    {
        _sprite.RotationDegrees = 0f;
        // Back to hidden, same as any other turn where nothing just
        // changed mood — not the woken-up emotion's emoji, since waking
        // up isn't itself a CurrentEmotion change.
        if (_emotionLabel != null)
            _emotionLabel.Visible = false;
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
            _attemptElapsed = 0f;
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

    private void ProcessAttempting(float delta)
    {
        // Hold here for a beat before actually resolving anything below
        // — see AttemptDuration's own comment. No movement, no visual
        // change needed for this wait itself; PlayerCharacter surfaces
        // its own "in progress" UI for the duration (see its
        // ActiveActionLabel), and an NPC's fixed idle pose already
        // reads fine as "doing something" for a second and a half.
        _attemptElapsed += delta;
        if (_attemptElapsed < AttemptDuration)
            return;

        // sleep no longer comes through here at all — AssignAction()
        // routes it straight to State.Sleeping, since it needs a real
        // duration and immobility, not an instant resolve.
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

        // "trade" and "steal" both resolve directly against another
        // character rather than a world resource — neither is
        // IInteractable (there's no "gathering rules" to ask a person),
        // and the target here is literally another NPCActor (or
        // PlayerCharacter, since it IS one).
        if (CurrentAction.Id == "trade" || CurrentAction.Id == "steal")
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
