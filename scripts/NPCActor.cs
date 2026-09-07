using Godot;
using System.Collections.Generic;

// The tactical layer. Give it a GameAction and it handles everything
// physical about carrying it out — walking toward the target, checking
// range every physics frame, retrying the approach on its own — without
// asking the mind anything again until the action is truly resolved.
// That resolution (success or a terminal failure) is the only thing
// that goes back upstream, via ActionCompleted.
public partial class NPCActor : CharacterBody2D, ICombatant
{
    [Signal]
    public delegate void ActionCompletedEventHandler(Godot.Collections.Dictionary result);

    // Only ever emitted under GameSettings.PermadeathEnabled — see
    // ReceiveDamage(). Main listens for this on every NPC and the
    // player to actually remove them from the game / show a real
    // game-over, since NPCActor itself has no way to unregister from
    // WorldRegistry or stop its own NpcAgent's turn loop.
    [Signal]
    public delegate void DownedEventHandler();

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
    protected enum State { Idle, Navigating, Attempting, Sleeping, Incapacitated }
    protected State _state = State.Idle;

    public GameAction CurrentAction;

    // "In fight mode" for a while after actually taking a hit, not
    // forever, and not just from being near something dangerous — a
    // real bidirectional "still targeting me?" link back to whichever
    // Animal is attacking would need signaling both ways for no real
    // benefit; a rolling time window off the last hit already answers
    // "am I currently under threat" correctly for CanSleep()'s purposes
    // (an animal that gave up the chase stops landing hits, and this
    // clears itself a few seconds later).
    private float _timeSinceAttacked = 999f;
    private const float ThreatWindowSeconds = 8f;
    public bool UnderThreat => _timeSinceAttacked < ThreatWindowSeconds;

    // Whoever landed the most recent hit — read by NpcAgent's fight/
    // flee/freeze handling the same way Animal.LastAttacker already
    // lets a species' own DecideBehavior() know who to defend against.
    // Not cleared on a timer the way _timeSinceAttacked/UnderThreat is;
    // a stale reference here is harmless since nothing reads this
    // without also checking UnderThreat (or, for FFF, its own fresh
    // ActiveThreats() scan) first.
    public ICombatant LastAttacker { get; private set; }

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

    // Which of the 4 drawn facings (CharacterSpriteBuilder.Down/Up/
    // Left/Right) is currently showing — updated only while actually
    // moving (UpdateSpriteFacing), held otherwise, same as a real
    // character doesn't un-face a direction just by standing still.
    // Down by default: it's the pose CharacterSpriteBuilder.Build()
    // already uses for the very first idle frame, so a freshly-spawned,
    // not-yet-moved character's remembered facing matches what's
    // actually on screen.
    private string _facing = CharacterSpriteBuilder.Down;

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

    // Above the emotion emoji/speech bubble, out of their way — see
    // VitalsBarDisplay's own header. Null for PlayerCharacter (see
    // _Ready() below) — the player already has a full VitalsPanel on
    // screen; a second copy floating over their own head would just be
    // redundant clutter they can't easily see past their own sprite
    // anyway. "Other NPC and animal stats," not the player's own.
    private VitalsBarDisplay _vitalsBars;

    // Same PlayerCharacter exemption as _vitalsBars above — set via
    // SetDisplayName(), not here (see that method's own comment).
    private Label _nameLabel;
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

    // "flee" walks toward a computed point, not a WorldRegistry entity
    // — there's nothing to look up (see GameAction.Destination's own
    // comment), so ProcessNavigating() reads this instead of
    // _targetNode.GlobalPosition when it's set.
    private Vector2? _fleeDestination;
    private const float FleeArrivalRange = 24f;

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

        // Same shared radial gradient/warm tint as FirePit's own light
        // — see RadialLightTexture's header. Modest radius: a torch is
        // a personal light, not a second visibility light (that's
        // PlayerCharacter.BuildVisibilityLight's job, and player-only).
        // Every NPCActor gets one (universal, not player-only, the same
        // "no hardcoded roles" reasoning as attack/eat/etc.) — hidden
        // until an actual torch is burning.
        _torchLight = new PointLight2D
        {
            Name = "TorchLight",
            Texture = RadialLightTexture.Get(),
            TextureScale = 260f / RadialLightTexture.Size, // ~130px radius
            Color = new Color(1f, 0.6f, 0.3f),
            Energy = 0.9f,
            Visible = false,
        };
        AddChild(_torchLight);

        // A held torch, drawn over the character sprite rather than
        // baked into the sheet — see TorchIconTexture's header. Child of
        // _sprite (not a sibling like _torchLight) so it rides along
        // with every transform _sprite already gets — most notably the
        // 90° "lying down" rotation Sleeping/Incapacitated use, which
        // should carry the torch along with the body rather than leave
        // it standing upright over a horizontal character. The parent's
        // own texture flips (or rather, swaps to a different drawn
        // facing) independently of a child's position though, so
        // UpdateSpriteFacing() mirrors TorchHandOffset by hand. Visibility follows _torchLight's, set
        // wherever that is below — one hasTorch check driving both.
        _torchSprite = new Sprite2D
        {
            Name = "TorchSprite",
            Texture = TorchIconTexture.Get(),
            Centered = false,
            Offset = new Vector2(-TorchIconTexture.Width / 2f, -TorchIconTexture.Height),
            Position = TorchHandOffset,
            Visible = false,
        };
        _torchSprite.TextureFilter = TextureFilterEnum.Nearest;
        _sprite.AddChild(_torchSprite);

        BuildEmotionLabel();
        BuildSpeechBubble();
        // Deliberately no initial flash here — showing Neutral the
        // moment a character spawns isn't a "mood change," it's just
        // the starting state; the emoji only appears once CurrentEmotion
        // actually changes to something.

        // See _vitalsBars' own field comment for why PlayerCharacter
        // skips this — `this is not PlayerCharacter`, not a virtual
        // hook, since there's nothing else here any subclass needs to
        // customize.
        if (this is not PlayerCharacter)
        {
            _vitalsBars = new VitalsBarDisplay { Position = new Vector2(0f, SpriteTopY - 10f) };
            AddChild(_vitalsBars);

            // Text set later, by SetDisplayName() — NPCActor itself has
            // no idea what its own name is (that's Personality.Name,
            // owned by NpcAgent, not this tactical-layer class); the
            // node still needs to exist now so NpcFactory has
            // something to call into right after construction, same
            // "configure after the fact" shape SetCharacterSprite()
            // already uses for its own sprite variant.
            _nameLabel = new Label { Position = new Vector2(0f, SpriteTopY - 40f) };
            _nameLabel.AddThemeFontSizeOverride("font_size", 13);
            _nameLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.85f));
            _nameLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
            _nameLabel.AddThemeConstantOverride("outline_size", 3);
            AddChild(_nameLabel);
        }
    }

    // Called once by NpcFactory right after construction — see
    // _nameLabel's own comment for why this can't just be known at
    // _Ready() time. A no-op for PlayerCharacter (there's no label to
    // set — nothing else calls this for the player anyway).
    public void SetDisplayName(string name)
    {
        if (_nameLabel == null) return;
        _nameLabel.Text = name;
        // Re-centered now that the label has real text/width — Godot
        // doesn't retroactively re-run HorizontalAlignment centering
        // against the node's own origin the way a Control anchored in
        // a container would; a bare Label positioned directly in
        // world space just grows from its top-left corner, so this
        // shifts it left by half its own measured width instead.
        _nameLabel.Position = new Vector2(-_nameLabel.GetMinimumSize().X / 2f, _nameLabel.Position.Y);
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
        // "Cannot sleep while in fight mode" — checked first, overrides
        // every other tier: exhausted or not, near home or not, lying
        // down while something's actively after you is never on offer.
        if (UnderThreat)
            return false;
        if (Vitals.Fatigue > Vitals.SleepUnnecessaryThreshold)
            return false;
        if (Vitals.NeedsSleep)
            return true;
        return GlobalPosition.DistanceTo(homePosition) <= ActionRanges.SleepNearHome;
    }

    // The Health-side equivalent of CanSleep() — offered once Health
    // starts actually dropping, not only once it's critical, OR once
    // Hunger itself is getting low (Vitals.NeedsFood — the more common,
    // everyday reason to eat now that Hunger decays passively for
    // everyone, same as Fatigue), and only when there's real food on
    // hand to eat (Food.BestFoodIn already returns null otherwise, so
    // this can't offer a dead-end choice).
    public const float EatHealthThreshold = 80f;
    public bool CanEat() => (Vitals.Health < EatHealthThreshold || Vitals.NeedsFood) && Food.BestFoodIn(Inventory) != null;

    // "The torch should become a normal stick after 5min too — needs a
    // fireplace to relight it again." A single shared burn-down timer
    // per character, not per torch instance — Inventory only tracks
    // item COUNTS, not individual item state, so this treats "how long
    // until my torch(es) go out" as one clock this character is
    // carrying, not something attached to any specific torch. Making a
    // NEW torch while one's already burning (RefreshTorch, called from
    // FirePit.TryInteract's "make_torch") just resets that shared
    // clock back to full — a real simplification (a torch made near
    // the end of the last one's burn arguably "should" get its own
    // fresh 5 minutes, and in this model it effectively does, at the
    // cost of any OTHER still-burning torch also resetting alongside
    // it) rather than tracking N independent countdowns for what's, in
    // practice, never going to be more than one or two at a time.
    public const float TorchDuration = 300f; // 5 minutes, matching FirePit.LitDuration
    private float _torchTimer;
    private PointLight2D _torchLight;
    private Sprite2D _torchSprite;

    // Local to _sprite, in its own pre-scale pixel space (same space
    // Offset above uses) — near the right hand of a character facing
    // right; UpdateSpriteFacing() negates the X half when _facing is
    // Left (and leaves it as-is for Up/Down — there's no separate
    // held-torch art per facing, just the one hand-height anchor,
    // mirrored or not).
    // Y was originally -9 (mid-torso), but with the torch's own height
    // added on top of that anchor, the flame landed up by the ear
    // instead of a hand held at the side — lower anchor, same shape.
    private static readonly Vector2 TorchHandOffset = new(6f, -4f);

    public void RefreshTorch() => _torchTimer = TorchDuration;

    // --- ICombatant ---
    public int StrengthMod => Stats.StrengthMod;
    public int DexterityMod => Stats.DexterityMod;

    // Already down either way (knocked out and waiting to recover, or
    // permanently down under permadeath) — either reads as "can't be
    // attacked again" to Combat's own target-availability checks.
    public bool IsDown => _state == State.Incapacitated || _permanentlyDown;
    private bool _permanentlyDown = false;

    private const float IncapacitatedDuration = 15f;

    // The one place ANY damage to a human — animal attack, later maybe
    // player-vs-player — actually lands. Interrupts whatever this
    // character was doing (including, per "if attacked, will always
    // wake up from sleep," Sleeping specifically) and, once Health
    // bottoms out, either knocks them out (recoverable) or puts them
    // permanently down, depending on GameSettings.PermadeathEnabled.
    public void ReceiveDamage(int amount, ICombatant attacker = null)
    {
        if (IsDown) return; // already down, no piling on
        _timeSinceAttacked = 0f; // drives UnderThreat — "cannot sleep while in fight mode"
        LastAttacker = attacker;

        // A hit interrupts whatever this character was doing —
        // sleeping always ends on one ("if attacked, will always wake
        // up from sleep"), and so does any other PEACEFUL action in
        // progress (walking to a tree, mid-gather, mid-travel, an
        // earlier "freeze" wait, ...): "the LLM can't choose to pick
        // berries while a wolf is attacking them." An already
        // in-progress "attack" or "flee" is deliberately exempt from
        // this, though — those ARE the combat response already under
        // way, and unconditionally restarting the decision on every
        // single incoming hit would mean neither could ever actually
        // resolve: a wolf's own attack cooldown (1.2s) is faster than
        // a human's own AttemptDuration (1.5s), so attacking back
        // could never land a hit, and fleeing could never actually put
        // distance behind it, if every hit taken along the way kept
        // canceling it first and restarting the decision from
        // scratch. Confirmed as a real bug this way, empirically, not
        // just reasoned about — a real wolf genuinely could not be
        // fought off before this exemption was added. Every branch
        // that DOES interrupt goes through the same Finish(), which
        // fires ActionCompleted right away — NpcAgent's
        // OnActionCompleted gives the "attacked"/"attacked_while_sleeping"
        // reasons a zero-pause fast path specifically so this reads as
        // an instant reflex, not a decision that waits behind the
        // normal per-turn pacing.
        bool wasSleeping = _state == State.Sleeping;
        bool isCombatInProgress = !wasSleeping && CurrentAction != null &&
            (CurrentAction.Id == "attack" || CurrentAction.Id == "flee");
        bool wasActing = CurrentAction != null;
        if (wasSleeping)
        {
            EndSleepVisual();
            Finish(false, "attacked_while_sleeping");
        }
        else if (wasActing && !isCombatInProgress)
        {
            Finish(false, "attacked");
        }

        Vitals.Damage(amount);
        if (Vitals.IsIncapacitated)
            HandleIncapacitation("incapacitated");
    }

    // The actual down-transition, shared by two very different
    // triggers: a real hit (ReceiveDamage, above — Vitals.Damage
    // brought Health to 0) and passive starvation (_PhysicsProcess,
    // below — Vitals.DecayOverTime() drained it instead, once
    // Vitals.IsStarving). Both end up in exactly the same place —
    // knocked out (recoverable) or permanently down, depending on
    // GameSettings.PermadeathEnabled — because Health hitting 0 means
    // the same thing regardless of how it got there.
    private void HandleIncapacitation(string reason)
    {
        // Whatever was in progress needs to resolve/notify one way or
        // another before this character goes down. ReceiveDamage()
        // already interrupted (and Finish()'d) anything peaceful, or a
        // sleeping character, before calling this — so for a
        // combat-caused incapacitation, this only ever still finds
        // something left in CurrentAction when it was an exempted,
        // still-in-flight attack/flee (see ReceiveDamage's own
        // comment for why those are exempt from the earlier
        // interrupt). Starvation reaches here directly from
        // _PhysicsProcess, though, with NOTHING interrupted yet —
        // including possibly still being asleep — so both cases are
        // handled right here rather than assumed already done.
        // Skipping this Finish() call would mean ActionCompleted never
        // fires for whatever was in progress, and NpcAgent's own turn
        // loop (which only ever resumes via that signal) would
        // silently stall forever waiting for a call that was never
        // coming — the same class of bug already found and fixed in
        // TakeTurn()'s own incapacitation wait loop.
        if (_state == State.Sleeping)
            EndSleepVisual();
        if (CurrentAction != null)
            Finish(false, reason);

        if (GameSettings.PermadeathEnabled)
        {
            _permanentlyDown = true;
            Velocity = Vector2.Zero;
            BeginIncapacitatedVisual(permanent: true);
            EmitSignal(SignalName.Downed);
        }
        else
        {
            _state = State.Incapacitated; // overrides whatever Finish() above left _state as (Idle) — single-threaded, so nothing runs between the two
            _elapsed = 0f;
            BeginIncapacitatedVisual(permanent: false);
        }
    }

    private void ProcessIncapacitated(float delta)
    {
        // No movement, same as Sleeping — being knocked out is
        // immobile by construction, not a separate "don't let WASD
        // move you" check.
        _elapsed += delta;
        if (_elapsed < IncapacitatedDuration)
            return;

        Vitals.RecoverFromKnockout();
        EndIncapacitatedVisual();
        _state = State.Idle;
    }

    // Reuses the same "lying down" rotation Sleeping already uses (no
    // separate knocked-out/dead pose to draw) but a different emoji
    // than 💤 so the two read as visually distinct at a glance — 💫 for
    // a recoverable knockout, 💀 when GameSettings.PermadeathEnabled
    // made this real. Same emotion-token-bump trick BeginSleepVisual()
    // uses, for the same reason (a stale mood-change hide-timer
    // shouldn't be able to wipe this mid-recovery either).
    private void BeginIncapacitatedVisual(bool permanent)
    {
        _sprite.RotationDegrees = 90f;
        if (_emotionLabel != null)
        {
            _emotionToken++;
            _emotionLabel.Text = permanent ? "💀" : "💫";
            _emotionLabel.Visible = true;
        }
    }

    private void EndIncapacitatedVisual()
    {
        _sprite.RotationDegrees = 0f;
        if (_emotionLabel != null)
            _emotionLabel.Visible = false;
    }

    // Called once, right after construction — NpcFactory picks a variant
    // per NPC, PlayerCharacter.Initialize() picks its own. See
    // CharacterSpriteBuilder for what "variant" actually selects.
    public void SetCharacterSprite(int variantIndex)
    {
        _sprite.SpriteFrames = CharacterSpriteBuilder.Build(variantIndex);
        _sprite.Play("idle_" + _facing);
    }

    // Picks which of the 4 drawn facings (CharacterSpriteBuilder.Down/
    // Up/Left/Right) matches how this character is actually moving, and
    // switches between that facing's idle/walk animation based on
    // whether Velocity is nonzero right now — reads the same Velocity
    // every mover here already sets (ProcessNavigating's MoveAndSlide()
    // call, or PlayerCharacter's free-movement branch), so both an
    // NPC's assigned navigation and the player's WASD input animate
    // identically without either needing to know this exists. Protected,
    // not private — PlayerCharacter calls it again after its own idle-
    // movement branch sets Velocity, since that happens after this
    // class's own _PhysicsProcess (and this call within it) already ran
    // for the frame; everywhere else, this one call is all that's
    // needed.
    //
    // Dominant-axis: whichever of X/Y Velocity is larger in magnitude
    // decides left/right vs up/down, same as any 4-direction top-down
    // character picks a single facing out of free-form movement. Only
    // reconsidered while actually moving — standing still keeps
    // whatever _facing was last moving toward, the same way a real
    // person doesn't spin to face some default direction the instant
    // they stop walking.
    protected void UpdateSpriteFacing()
    {
        if (_sprite?.SpriteFrames == null)
            return;

        bool moving = Velocity.Length() > 1f;
        if (moving)
        {
            _facing = Mathf.Abs(Velocity.X) > Mathf.Abs(Velocity.Y)
                ? (Velocity.X < 0f ? CharacterSpriteBuilder.Left : CharacterSpriteBuilder.Right)
                : (Velocity.Y < 0f ? CharacterSpriteBuilder.Up : CharacterSpriteBuilder.Down);
        }

        // Keeps the torch on whichever hand faces outward — mirrored
        // for Left, left as-is otherwise (Up/Down have no separate
        // held-torch art, just the one hand-height anchor).
        if (_torchSprite != null)
        {
            float x = _facing == CharacterSpriteBuilder.Left ? -TorchHandOffset.X : TorchHandOffset.X;
            _torchSprite.Position = new Vector2(x, TorchHandOffset.Y);
        }

        string wanted = (moving ? "walk_" : "idle_") + _facing;
        if (_sprite.Animation != wanted)
            _sprite.Play(wanted);
    }

    public void AssignAction(GameAction action)
    {
        // Can't act while down — ProcessIncapacitated() clears this on
        // its own after IncapacitatedDuration, or (permadeath) never.
        // Without this, an NpcAgent turn that fires again mid-recovery
        // (its own TakeTurn() already avoids this — see the "while
        // (Actor.IsDown)" wait loop there — but nothing stops a stray
        // call otherwise) would silently overwrite Incapacitated with
        // whatever it just decided.
        if (_state == State.Incapacitated)
            return;

        CurrentAction = action;
        _elapsed = 0f;
        // Cleared unconditionally on every new assignment, not just a
        // non-flee one — otherwise a stale point from an EARLIER flee
        // would silently keep steering ProcessNavigating() for
        // whatever gets assigned next, since flee's own branch below
        // is the only thing that ever sets this again.
        _fleeDestination = null;

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

        // "flee" — a fight/flee/freeze response with nowhere real to
        // look up (see GameAction.Destination's own comment): walk
        // straight toward a computed point rather than any registered
        // entity, no A* routing (getting clear of danger fast matters
        // more here than a tidy route around obstacles).
        if (action.Id == "flee")
        {
            _fleeDestination = action.Destination ?? GlobalPosition;
            _targetNode = null;
            _waypoints = null;
            _waypointIndex = 0;
            _state = State.Navigating;
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
        // at the target. "follow", "trade", "steal", and "attack" all
        // target something that MOVES (an NPCActor/PlayerCharacter, or
        // an Animal) — a path computed once at the instant this action
        // starts would go stale the moment the target takes a step, so
        // all four always walk straight at wherever the target
        // currently is too (the same live GlobalPosition read every
        // physics frame that already makes following work at all).
        // Everything else is a "known," stationary destination and
        // routes through A* when a route exists; a null result (no path
        // found) falls back to the same direct movement.
        _waypoints = (action.Id == "travel" || action.Id == "follow" || action.Id == "trade" || action.Id == "steal" || action.Id == "attack")
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
        _timeSinceAttacked += (float)delta;
        _vitalsBars?.Refresh(Vitals.Health / 100f, Vitals.Fatigue / 100f, Vitals.Hunger / 100f);

        // Torch burn-down — see RefreshTorch's own comment for why
        // this is one shared timer, not per-item. Counts down
        // regardless of state (a torch burns whether you're gathering,
        // walking, or asleep), and once it runs out, every "torch"
        // currently held turns back into a plain "stick" — "needs a
        // fireplace to relight it again," not just re-carry-able as a
        // torch forever.
        if (_torchLight != null)
        {
            // Tied to actually HOLDING a torch right now, not just to
            // whoever originally crafted it — a real bug before this:
            // _torchTimer used to keep counting down (and the light
            // stayed on) for whoever lit it even after they traded it
            // away, while whoever they gave it to got no light at all
            // despite now actually carrying it. Inventory.Has() is the
            // one source of truth for "do I have a torch," checked
            // fresh every tick, not just at craft/trade time.
            bool hasTorch = Inventory.Has("torch");
            if (!hasTorch)
            {
                // Nothing to burn down — also covers "just traded it
                // away," which is what makes the light turn off for
                // the giver immediately, the same tick, rather than
                // riding out whatever time was left on a timer that no
                // longer means anything for them.
                _torchTimer = 0f;
                _torchLight.Visible = false;
            }
            else
            {
                // Carrying one with no countdown already running —
                // either just received via trade/steal (the timer was
                // never started for THIS character) or, in principle,
                // some other way a torch ended up in Inventory without
                // going through RefreshTorch(). Starts a fresh full
                // burn rather than inheriting however much time the
                // previous holder had left — Inventory only tracks
                // item COUNTS, not per-item remaining time, so a
                // traded torch can't carry its exact remaining burn
                // with it; a fresh 5 minutes for the new holder is the
                // honest simplification here, not a stale timer stuck
                // at zero that would make it look permanently unlit.
                if (_torchTimer <= 0f)
                    _torchTimer = TorchDuration;

                _torchTimer -= (float)delta;
                _torchLight.Visible = true;
                if (_torchTimer <= 0f)
                {
                    int burnedOut = Inventory.Count("torch");
                    Inventory.Remove("torch", burnedOut);
                    Inventory.Add("stick", burnedOut);
                    _torchLight.Visible = false;
                }
            }

            // One hasTorch check driving both — the light and the held-
            // torch sprite always agree, since the sprite has no
            // independent state of its own to fall out of sync from.
            if (_torchSprite != null)
                _torchSprite.Visible = _torchLight.Visible;
        }

        // Passive starvation can bring Health to 0 on its own, with no
        // hit ever landing (DecayOverTime() above already applies
        // StarvationDamagePerSecond once Vitals.IsStarving) — this is
        // the other caller of HandleIncapacitation(), the one
        // ReceiveDamage() doesn't cover. !IsDown guards against calling
        // it again every single frame after the first, since Health
        // just sits at 0 (not below) for as long as this keeps being
        // true.
        if (Vitals.IsIncapacitated && !IsDown)
            HandleIncapacitation("starved");

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
            case State.Incapacitated:
                ProcessIncapacitated((float)delta);
                break;
        }

        // Sleeping/Incapacitated both drive their own visual (rotated
        // sprite, no walk/idle switching) via their own Begin/End visual
        // methods — skip the normal facing/animation logic entirely
        // instead of fighting it every frame.
        if (_state != State.Sleeping && _state != State.Incapacitated)
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

        Vector2 finalTarget;
        float arrivalRange;
        if (_fleeDestination.HasValue)
        {
            // No entity to go stale on (see AssignAction's flee
            // branch) — a computed point is always "still there."
            finalTarget = _fleeDestination.Value;
            arrivalRange = FleeArrivalRange;
        }
        else
        {
            if (_targetNode == null || !IsInstanceValid(_targetNode))
            {
                Finish(false, "target_not_found");
                return;
            }
            finalTarget = _targetNode.GlobalPosition;
            arrivalRange = CurrentAction.Range;
        }

        float distToFinal = GlobalPosition.DistanceTo(finalTarget);

        // Arriving within Range always wins, regardless of whether a
        // path is still being followed — a route that happens to pass
        // close to the target shouldn't force walking all the way to
        // the last waypoint first.
        if (distToFinal <= arrivalRange)
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
        // duration and immobility, not an instant resolve. eat IS
        // targetless (there's nowhere to walk to, just something in
        // your own pack) but still needs real handling, so it's
        // intercepted here, before the generic no-op branch below.
        if (CurrentAction.Id == "eat")
        {
            string food = Food.BestFoodIn(Inventory);
            if (food == null)
            {
                Finish(false, "no_food");
                return;
            }
            Inventory.Remove(food, 1);
            Vitals.Hunger = Mathf.Min(100f, Vitals.Hunger + Food.HungerFor(food));
            Vitals.Health = Mathf.Min(100f, Vitals.Health + Food.HealthFor(food));
            Finish(true, "ok", new Godot.Collections.Dictionary { { "item", food }, { "health", Vitals.Health }, { "hunger", Vitals.Hunger } });
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

        // "attack" — the one universal fighting mechanism (Combat.
        // Resolve), reused whether the target is an Animal or another
        // NPCActor. Weapon damage comes from whatever's actually in
        // Inventory (Weapons.BestDamage) — a stick if carried, bare
        // hands otherwise.
        if (CurrentAction.Id == "attack")
        {
            if (_targetNode is not ICombatant target || !IsInstanceValid(_targetNode))
            {
                Finish(false, "target_not_found");
                return;
            }
            if (target.IsDown)
            {
                Finish(false, "target_already_down");
                return;
            }
            Combat.Result combatResult = Combat.Resolve(Stats.StrengthMod, Stats.DexterityMod, target.DexterityMod, Weapons.BestDamage(Inventory));
            var data = combatResult.Check.ToData("attack");

            // Visual only — nothing here affects the roll or its
            // result, just makes it readable on screen: a quick lunge
            // toward whoever's being swung at, and a floating number
            // (or "miss") above them a moment later, the same way the
            // console log already shows this in text.
            CombatVisuals.PlayLunge(_sprite, target.GlobalPosition);

            if (combatResult.Hit)
            {
                target.ReceiveDamage(combatResult.Damage, this);
                data["damage"] = combatResult.Damage;
                CombatVisuals.ShowDamage(GetParent(), target.GlobalPosition, combatResult.Damage);
            }
            else
            {
                CombatVisuals.ShowMiss(GetParent(), target.GlobalPosition);
            }
            Finish(combatResult.Hit, combatResult.Hit ? "hit" : "missed", data);
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
        _fleeDestination = null;
        _state = State.Idle;
        EmitSignal(SignalName.ActionCompleted, result);
    }
}
