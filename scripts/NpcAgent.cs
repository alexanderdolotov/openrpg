using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Everything one NPC needs to run its own cognition loop: a reference to
// its actor (NOT a scene-tree child of this — see Initialize()), its
// own Mind (with its own ILlmProvider instance — never shared between
// agents, since a single HttpRequest node can't have two requests in
// flight at once), its memory, and its personality. Built by
// NpcFactory, not constructed directly.
//
// Main owns the shared world (WorldContext); an NpcAgent only reads
// that to build its own perception and fallback — it never mutates
// anything outside its own Actor/Memory.
//
// Note on Id vs Personality.Name: Id ("npc_0") is the stable internal
// key — WorldRegistry, thought-log correlation, self-exclusion checks.
// Personality.Name ("Maren") is what every human/LLM-facing surface
// uses instead — console log prefixes, SpeechLog, Memory records,
// perception's "who's nearby" line, and follow's target — because
// "npc_0 said: ..." is a worse thing for a model (or a person reading
// the log) to have to track than "Maren said: ...".
public partial class NpcAgent : Node, IWorldCharacter
{
    public string Id { get; private set; }
    public Personality Personality { get; private set; }
    public NPCActor Actor { get; private set; }
    public Mind Mind { get; private set; }
    public NpcMemory Memory { get; } = new();
    public SpatialMemory Spatial { get; } = new();

    // IWorldCharacter — the generic surface PlayerCharacter also
    // implements, so WorldContext.Agents and everything that reads it
    // (perception's "who's nearby," NearbyNpcNames(), SpeechLog calls)
    // don't need to know or care whether they're looking at an
    // LLM-driven NPC or the player.
    public string DisplayName => Personality.Name;
    public Vector2 GlobalPosition => Actor.GlobalPosition;
    public Emotion CurrentEmotion => Actor.CurrentEmotion;

    private WorldContext _world;
    private NpcThoughtLogger _thoughtLog;
    private Action<string, string> _uiLog; // (line, colorHex) — Main.Log, so console formatting stays centralized
    private bool _pureLlmMode;

    private bool _thinking = false;

    // What just happened, kept outside Memory on purpose — this needs
    // to be visible to the model every single turn regardless of
    // whether NpcMemory has compressed recently, since a compression
    // pass folding this into vague diary prose (which it did, in
    // practice, before the summarize prompt was tightened) is exactly
    // how a repeated failure stopped being visible at all.
    private string _lastResultLine = "";
    private string _lastFailureKey = "";
    private int _consecutiveFailures = 0;

    // Noticing an inventory change caused by someone ELSE (a trade
    // received, a theft) rather than this NPC's own last action —
    // AbsorbOwnChange() runs right after this NPC's own action resolves
    // (see OnActionCompleted), DetectExternalChanges() at the top of the
    // following TakeTurn(). See InventoryWatcher itself for why this is
    // shared with PlayerCharacter rather than each carrying its own copy.
    private readonly InventoryWatcher _inventoryWatcher = new();

    // Set once this NPC agrees to the player's invitation to follow —
    // see HandleDirectPlayerRequest/StartFollowCommitment. While active
    // and not yet expired, TakeTurn() mostly skips the normal per-turn
    // Decide() for this NPC and just re-issues follow directly, rather
    // than trusting the model to keep re-choosing it turn after turn on
    // its own: ActInstruction already asks for exactly that ("keep
    // choosing follow again"), but a small model drifts off it in
    // practice, and arriving next to the player once used to read as
    // "done" the instant something else nudged the next turn's choice
    // elsewhere. This makes "walk alongside the player for a while" an
    // actual few-minutes-long commitment instead of a one-step courtesy
    // that quietly ends the moment they're caught up to.
    //
    // Deliberately NOT an absolute lock for the whole window, though —
    // FollowCommitmentSeconds is a target duration, not a guarantee. It
    // still ends early, immediately, for two reasons that already short-
    // circuit TakeTurn() ahead of this: real danger (DetectThreatSituation
    // is checked long before this) and a fresh line from the player
    // (playerRequestMessage, checked just above this). On top of that,
    // every FollowCommitmentRecheckEvery-th turn genuinely reopens the
    // question to a real Mind.Decide() call instead of forcing follow —
    // see the recheck branch below and UpdateFollowCommitmentAfterDecision
    // — so personality and mood get an honest, periodic chance to change
    // course on their own too, the same as any other ongoing goal would.
    private string _followCommitmentTarget;
    private ulong _followCommitmentEndMsec;
    private int _followCommitmentTurnsSinceCheck;
    private const float FollowCommitmentSeconds = 180f; // "a few minutes" — a target, not a hard deadline
    private const int FollowCommitmentRecheckEvery = 3; // roughly one genuine reconsideration every few forced turns

    private static readonly Random Rng = new();
    private const float RetryDelaySeconds = 3f;

    // A floor under every action's post-result pause, not just wait's/
    // sleep's own — previously every OTHER action (pick_apple, speak,
    // travel, trade, steal, ...) had none at all, so the
    // instant an action resolved, TakeTurn() ran again immediately. With
    // 3 NPCs and a fast model, that reads as a wall of thought/speech/
    // result lines with no time to actually read any of them. Purely a
    // pacing knob for a human watching the console — raise it for a
    // more relaxed pace, lower it (toward 0) to go back to "as fast as
    // the model allows."
    private const float MinTurnPause = 4f;

    // Godot Nodes are constructed parameterless and configured
    // afterward — this plays that role, since an agent needs several
    // collaborators wired before it can run.
    public void Initialize(
        string id,
        Personality personality,
        NPCActor actor,
        ILlmProvider provider,
        WorldContext world,
        NpcThoughtLogger thoughtLog,
        Action<string, string> uiLog,
        bool pureLlmMode)
    {
        Id = id;
        Personality = personality;
        Actor = actor;
        Mind = new Mind(provider);
        _world = world;
        _thoughtLog = thoughtLog;
        _uiLog = uiLog;
        _pureLlmMode = pureLlmMode;

        // NOT AddChild(Actor) — the caller (NpcFactory) already added
        // Actor directly to the world layer itself, as a sibling of this
        // NpcAgent rather than a child of it. See NpcFactory's own
        // comment for why: Actor is the only part of this that draws
        // anything, and it needs to sit at the SAME tree depth
        // PlayerCharacter does for Y-sort to have one uniform rule to
        // apply, not "usually direct children, except NPCs, which are
        // one level deeper."
        AddChild((Node)provider);
        Actor.ActionCompleted += OnActionCompleted;
    }

    public void Start() => _ = TakeTurn();

    private async Task TakeTurn()
    {
        if (_thinking) return;
        _thinking = true;

        // While incapacitated, there's nothing to decide — and
        // AssignAction() silently refuses to assign anything while
        // Actor.IsDown regardless (see its own comment), so deciding
        // anyway would just waste a real LLM call. Waiting it out HERE,
        // rather than letting that silent refusal happen, is also what
        // lets the turn loop actually resume once NPCActor's own
        // ProcessIncapacitated() clears it — an assign attempt that
        // goes nowhere never calls Finish(), and Finish() firing
        // ActionCompleted is the ONLY thing that ever calls TakeTurn()
        // again; without this wait, one incapacitation would silently
        // and permanently stall this NPC's whole turn loop, even long
        // after it physically recovers.
        while (Actor.IsDown)
        {
            if (GameSettings.PermadeathEnabled)
            {
                // Permanently down under permadeath — never recovers.
                // Stop trying entirely rather than poll forever waiting
                // for a recovery that isn't coming.
                _thinking = false;
                return;
            }
            _uiLog($"[{Personality.Name}] ...down, waiting to recover...", "6f8068");
            await Actor.ToSignal(Actor.GetTree().CreateTimer(3f, processAlways: false), SceneTreeTimer.SignalName.Timeout);
        }

        // Checked before anything else this turn commits to — "the LLM
        // can't choose to go pick berries while a wolf is attacking
        // them." Doesn't skip the perception-building steps below
        // (heard speech, witnessed events, discovery) — those still
        // happen and still get remembered — it just means what
        // happens with that perception afterward is the narrow fight/
        // flee/freeze decision (HandleThreatTurn) instead of the
        // normal full-menu one.
        ThreatSituation threatSituation = DetectThreatSituation();
        bool underThreat = threatSituation != null;
        string dangerLabel = underThreat && !threatSituation.SelfTargeted ? "thinking (friend in danger!)..." : "thinking (danger!)...";
        _uiLog(underThreat ? $"[{Personality.Name}] ...{dangerLabel}" : $"[{Personality.Name}] ...thinking...", underThreat ? "e0876b" : "6f8068");

        NoticeInventoryChanges();
        Memory.Record("location", DescribeLocation());

        List<string> newlyDiscovered = Spatial.Observe(Actor.GlobalPosition, AllLandmarks());
        foreach (string id in newlyDiscovered)
        {
            _uiLog($"[{Personality.Name}] discovered {id} for the first time", "e0c66a");
            Memory.Record("discovery", $"Reached {id} for the first time.");
            _thoughtLog.Log(Personality.Name, "DISCOVERY", id);
        }

        // Delivered once per listener (SpeechLog marks it consumed),
        // and written into Memory rather than just this turn's
        // perception — an utterance heard now should still be
        // rememberable turns later, not just while it happens to still
        // be live. ALSO collected into freshHeard below, for
        // BuildPerception() to surface as its own prominent block —
        // Memory alone buries it inside a long chronological "Recently:"
        // dump next to location/action/discovery entries, which turned
        // out to be genuinely too easy for a small model to skim past
        // entirely: an NPC directly asked "come follow me to fight a
        // wolf" would confirm hearing it (the persuasion-hint line
        // showed it landing as convincing) and then just... not react
        // to it at all, next turn, in either thought or action. A
        // fresh, clearly-labeled "someone just said this to you" line
        // right in the main perception body — not buried in the diary —
        // is what actually gives a small model a real shot at
        // responding to it.
        var freshHeard = new List<string>();
        // Captured alongside freshHeard below, not derived from it —
        // used right after this loop to route straight into a guaranteed
        // direct answer (see HandleDirectPlayerRequest) instead of
        // trusting the full-menu decision to get to it on its own. Last
        // one wins on the rare turn the player says more than one thing
        // at once; there's only one answer to give this turn regardless.
        string playerRequestSpeaker = null;
        string playerRequestMessage = null;
        foreach ((string speakerName, string message) in SpeechLog.Overheard(Personality.Name, Actor.GlobalPosition))
        {
            string hint = DescribePersuasionHint(speakerName);
            // "It would be more fun if most of them actually followed
            // me... they're too independent right now." Marking the
            // real human player as such, distinctly from another NPC's
            // own small talk, is what lets ThinkInstruction/
            // ActInstruction single out "a direct request from the
            // actual player" for the extra weight it's meant to carry
            // — DescribePersuasionHint's own roll already leans that
            // way mechanically (see its own comment), but a small
            // model still needs to be told in plain words which lines
            // that applies to.
            bool fromPlayer = IsPlayerName(speakerName);
            if (fromPlayer) { playerRequestSpeaker = speakerName; playerRequestMessage = message; }
            string speakerNote = fromPlayer ? " (the real human player, not another character in this world)" : "";
            _uiLog($"[{Personality.Name}] heard {speakerName} say: \"{message}\"{hint}", "c9a9e8");
            Memory.Record("heard", $"{speakerName} said: \"{message}\"{hint}");
            _thoughtLog.Log(Personality.Name, "HEARD", $"{speakerName}: {message}{hint}");
            freshHeard.Add($"{speakerName}{speakerNote} just said to you: \"{message}\"{hint}");
        }

        // Same "delivered once, written to Memory" treatment as heard
        // speech just above — whatever else happened nearby since this
        // NPC's own last turn (someone gathering, giving something
        // away, arriving somewhere, ...) arrives here as one batch, not
        // as it happens. That's what keeps this from turning into a
        // flood of interruptions: nothing here pushes into anyone's
        // context in real time, it just waits in WorldEventLog until
        // whichever NPC's turn comes around asks what it missed — see
        // RecentEventBuffer's own header for the full reasoning. Also
        // collected into freshWitnessed, same reasoning and same fix as
        // freshHeard above — "what others are doing" deserves the same
        // prominent placement "what others are saying" just got, not
        // just a line buried in Memory's chronological dump.
        var freshWitnessed = new List<string>();
        foreach ((string actorName, string description) in WorldEventLog.Witnessed(Personality.Name, Actor.GlobalPosition))
        {
            _thoughtLog.Log(Personality.Name, "WITNESSED", description);
            Memory.Record("witnessed", description);
            freshWitnessed.Add($"You just saw: {description}");
        }

        string perception = BuildPerception(freshHeard, freshWitnessed);

        if (underThreat)
        {
            await HandleThreatTurn(threatSituation, perception);
            return;
        }

        // No real fight happening, but a wolf or bear might still be
        // in sight — the lighter "alert" mode. Mechanical, no LLM call
        // (see its own section header for why); a "cautious" or "bold"
        // roll assigns an action directly and skips the normal turn
        // below entirely this cycle, same as danger does. An
        // "oblivious" roll (or no alert-worthy animal at all) falls
        // straight through to the normal full-menu turn, where
        // BuildPerception()'s own animal line already honestly
        // mentioned it regardless of what this NPC's own reflex did.
        Animal alertAnimal = DetectAlertAnimal();
        if (alertAnimal == null)
        {
            _lastAlertAnimalId = null;
        }
        else
        {
            GameAction alertAction = HandleAlert(alertAnimal);
            if (alertAction != null)
            {
                // MUST happen before this returns — every other early
                // return out of TakeTurn() either goes through a path
                // that already reset this (Mind.Decide/DecideThreatResponse
                // both do, right after their own await) or never set it
                // to begin with (the permadeath early-exit in the
                // IsDown wait loop above). This branch is synchronous,
                // with no await of its own, so nothing else was ever
                // going to reset it — skipping this line means
                // _thinking stays true forever the instant an NPC has
                // its first alert reaction, which silently freezes its
                // ENTIRE turn loop for the rest of the session (nothing
                // else ever calls TakeTurn() again, since that only
                // happens via ActionCompleted, which only fires for an
                // action THIS method actually assigned) — a real,
                // observed bug, not a hypothetical one.
                _thinking = false;
                string alertTargetNote = alertAction.TargetId != "" ? $" -> {alertAction.TargetId}" : "";
                _uiLog($"[{Personality.Name}] attempting: {alertAction.Id}{alertTargetNote}", "d8ddd0");
                _thoughtLog.Log(Personality.Name, "ACTION_ATTEMPT", $"{alertAction.Id}{alertTargetNote}");
                Actor.AssignAction(alertAction);
                return;
            }
        }

        // Built here, ahead of the two priority branches below, rather
        // than right before the normal Decide() call the way it used
        // to sit — HandleDirectPlayerRequest needs the exact same
        // "what's actually available right now" lists Decide() does
        // (it offers the same full tool menu, just under a different
        // instruction; see Mind.DecidePlayerRequest's own header), so
        // there'd otherwise be two copies of this to keep in sync.
        var targets = new Mind.AvailableTargets
        {
            TreeIds = TreeIds(),
            FishingSpotIds = FishingSpotIds(),
            PineTreeIds = PineTreeIds(),
            BerryBushIds = BerryBushIds(),
            TravelTargetIds = TravelTargetIds(),
            NearbyNpcNames = NearbyNpcNames(),
            CarriedItems = CarriedItems(),
            SleepAllowed = Actor.CanSleep(_world.Home.GlobalPosition),
            AnimalIds = AnimalIds(),
            StickIds = StickIds(),
            EatAllowed = Actor.CanEat(),
            // Same "always offered, walk there when assigned" shape as
            // deposit's own "home" target — no distance gate here, just
            // the fire pit's own actual state (and, for make_torch,
            // whether there's really a stick on hand to light).
            LightFireAllowed = !_world.FirePit.IsLit,
            MakeTorchAllowed = _world.FirePit.IsLit && Actor.Inventory.Has("stick"),
            CookMeatAllowed = _world.FirePit.IsLit && Actor.Inventory.Has("rabbit_meat"),
        };

        // A direct line from the real player takes priority over the
        // normal full-menu decision below — see Mind.DecidePlayerRequest's
        // own header for why this is a separate call under its own
        // instruction, same shape as danger's own HandleThreatTurn
        // above. Skipped while an alert reflex just fired (that already
        // returned above) — otherwise, this is the very next thing
        // checked, ahead of even the ordinary "keep following"
        // commitment below, since a fresh line from the player might be
        // changing that plan too ("actually, never mind" / "let's go
        // the other way").
        if (playerRequestMessage != null)
        {
            await HandleDirectPlayerRequest(playerRequestSpeaker, playerRequestMessage, perception, targets);
            return;
        }

        // Already agreed to walk with the player and still within the
        // commitment window — keep doing exactly that, mechanically,
        // most turns, rather than re-opening it to the full menu every
        // single turn. See _followCommitmentTarget's own header for why
        // this exists at all, and for why it's a periodic recheck, not
        // an unconditional lock for the whole duration.
        if (_followCommitmentTarget != null)
        {
            bool commitmentLive = Time.GetTicksMsec() < _followCommitmentEndMsec && FollowCommitmentTargetStillPresent();
            if (commitmentLive && _followCommitmentTurnsSinceCheck < FollowCommitmentRecheckEvery)
            {
                _followCommitmentTurnsSinceCheck++;
                _uiLog($"[{Personality.Name}] attempting: follow -> {_followCommitmentTarget} (still tagging along)", "d8ddd0");
                _thoughtLog.Log(Personality.Name, "ACTION_ATTEMPT", $"follow -> {_followCommitmentTarget} (committed)");
                Actor.AssignAction(new GameAction("follow", _followCommitmentTarget, ActionRanges.Follow));
                // MUST happen before this returns, exactly like the
                // alert branch above — this is a synchronous return
                // with no await of its own, so nothing else was ever
                // going to reset _thinking. Missing this line once
                // already froze an NPC's entire turn loop the moment it
                // hit its first forced-follow turn — a real, observed
                // bug (see the thought log this was caught from), not a
                // hypothetical one.
                _thinking = false;
                return;
            }
            if (!commitmentLive)
            {
                // Expired, or the player's no longer a registered
                // character (shouldn't normally happen mid-session).
                _followCommitmentTarget = null;
            }
            else
            {
                // Still within the window, but due for a genuine
                // reconsideration this turn — reset the counter and
                // fall through to the real Mind.Decide() call below.
                // UpdateFollowCommitmentAfterDecision, right after it,
                // is what actually ends the commitment if this NPC's
                // own honest answer turns out to be something else.
                _followCommitmentTurnsSinceCheck = 0;
            }
        }

        var result = await Mind.Decide(perception, targets, Personality);
        _thinking = false;

        if (!result.Ok && _pureLlmMode && !IsToolCallFailure(result.Error))
        {
            // Pure LLM mode: a genuinely UNREACHABLE backend gets no
            // substitute action, ever — stand still and retry rather
            // than let a fallback quietly stand in for a decision
            // nothing ever actually got a chance to make. A tool-call
            // failure (the model DID respond, it just didn't produce a
            // usable call) is handled in the else-branch below instead
            // — see IsToolCallFailure's own header for why that's a
            // different situation.
            _uiLog($"[{Personality.Name}] mind unreachable ({result.Error}) -> retrying in {RetryDelaySeconds:0}s (fallback disabled)", "e0c66a");
            _thoughtLog.Log(Personality.Name, "FALLBACK_DISABLED", result.Error);
            // processAlways:false — this timer (and every other one an
            // NPC's turn loop waits on) should actually freeze while the
            // game is paused, not keep ticking down in the background.
            await Actor.ToSignal(Actor.GetTree().CreateTimer(RetryDelaySeconds, processAlways: false), SceneTreeTimer.SignalName.Timeout);
            await TakeTurn();
            return;
        }

        GameAction action;
        if (result.Ok)
        {
            action = result.Action;
            if (action.Emotion.HasValue)
            {
                Actor.CurrentEmotion = action.Emotion.Value;
                _uiLog($"[{Personality.Name}] feeling: {action.Emotion.Value}", "e8b4d8");
                Memory.Record("emotion", action.Emotion.Value.ToWireString());
                _thoughtLog.Log(Personality.Name, "EMOTION", action.Emotion.Value.ToWireString());
            }
            if (!string.IsNullOrEmpty(result.Thought))
            {
                _uiLog($"[{Personality.Name}] thought: {result.Thought}", "a9c9e8");
                Memory.Record("thought", result.Thought);
                _thoughtLog.Log(Personality.Name, "THOUGHT", result.Thought);
            }
        }
        else if (IsToolCallFailure(result.Error))
        {
            // "Allow fallback if the LLM tool call fails — but declare
            // it in terminal so I know why." Distinct wording and a
            // distinct thought-log kind from the "mind unreachable"
            // case right below, on purpose — this is the model
            // responding but failing to produce a usable tool call
            // (bad/missing JSON, an off-menu action, an invalid
            // target), not the backend being down, and now falls back
            // even under PureLlmMode (see the guard above).
            _uiLog($"[{Personality.Name}] LLM tool call failed ({result.Error}) -> random fallback", "e0c66a");
            _thoughtLog.Log(Personality.Name, "TOOL_CALL_FAILED", result.Error);
            action = RandomFallback();
        }
        else
        {
            _uiLog($"[{Personality.Name}] mind unreachable ({result.Error}) -> random fallback", "e0c66a");
            _thoughtLog.Log(Personality.Name, "FALLBACK", result.Error);
            action = RandomFallback();
        }

        // Only ever has anything to do on a recheck turn (see the
        // commitment block above) — every other turn either has no
        // commitment at all, or already returned before reaching here.
        // A genuine choice to keep following the same person renews the
        // commitment silently (nothing to do here); anything else is
        // this NPC honestly changing its mind, which is exactly what
        // "gentle guideline, not a hard lock" means in practice.
        UpdateFollowCommitmentAfterDecision(action);

        string targetNote = action.TargetId != "" ? $" -> {action.TargetId}" : "";
        _uiLog($"[{Personality.Name}] attempting: {action.Id}{targetNote}", "d8ddd0");
        _thoughtLog.Log(Personality.Name, "ACTION_ATTEMPT", $"{action.Id}{targetNote}");
        Actor.AssignAction(action);
    }

    // The direct-request counterpart to HandleThreatTurn above — see
    // Mind.DecidePlayerRequest's own header for why this reuses the
    // normal tool menu under a separate, much more insistent
    // instruction rather than either a fixed yes/no schema or just
    // leaning harder on ActInstruction. Whatever comes back is a real
    // action this very turn — the player asked something and gets an
    // actual response, not silence while this NPC "thinks it over" for
    // several turns running (the exact failure this exists to close
    // off): speak (a plain decline, or any other reply) if the model
    // called speak, or whatever real activity it agreed to otherwise.
    // Only the follow case gets special handling here — see
    // StartFollowCommitment's own header for why that one specifically
    // needs to outlast this single turn.
    private async Task HandleDirectPlayerRequest(string playerName, string playerMessage, string perception, Mind.AvailableTargets targets)
    {
        Mind.MindResult result = await Mind.DecidePlayerRequest(perception, targets, Personality);
        _thinking = false;

        if (!result.Ok && _pureLlmMode && !IsToolCallFailure(result.Error))
        {
            // Same "genuinely unreachable backend, no substitute, ever"
            // posture pure LLM mode takes for the normal turn above — a
            // tool-call failure (the model DID respond) is handled in
            // the else-branch below instead, same reasoning as there.
            _uiLog($"[{Personality.Name}] mind unreachable ({result.Error}) -> retrying in {RetryDelaySeconds:0}s (fallback disabled)", "e0c66a");
            _thoughtLog.Log(Personality.Name, "FALLBACK_DISABLED", result.Error);
            await Actor.ToSignal(Actor.GetTree().CreateTimer(RetryDelaySeconds, processAlways: false), SceneTreeTimer.SignalName.Timeout);
            await HandleDirectPlayerRequest(playerName, playerMessage, perception, targets);
            return;
        }

        GameAction action;
        if (result.Ok)
        {
            action = result.Action;
            _uiLog($"[{Personality.Name}] decides (player request): {action.Id}{(action.TargetId != "" ? $" -> {action.TargetId}" : "")}", "a9c9e8");
            _thoughtLog.Log(Personality.Name, "PLAYER_REQUEST_DECISION", $"{action.Id}{(action.TargetId != "" ? $" -> {action.TargetId}" : "")}");
            // Same emotion bookkeeping the normal turn does right after
            // Mind.Decide() below — this path skips that call entirely,
            // so it has to repeat the handful of lines rather than
            // silently drop them.
            if (action.Emotion.HasValue)
            {
                Actor.CurrentEmotion = action.Emotion.Value;
                _uiLog($"[{Personality.Name}] feeling: {action.Emotion.Value}", "e8b4d8");
                Memory.Record("emotion", action.Emotion.Value.ToWireString());
                _thoughtLog.Log(Personality.Name, "EMOTION", action.Emotion.Value.ToWireString());
            }
        }
        else if (IsToolCallFailure(result.Error))
        {
            // The model responded but never produced a usable tool call
            // for this direct question/request (see IsToolCallFailure) —
            // still owed a real, in-character reply rather than dead
            // air, just not one that commits to actually doing anything.
            action = new GameAction("speak", "", 0f, message: "Sorry, give me a moment.");
            _uiLog($"[{Personality.Name}] LLM tool call failed during player request ({result.Error}) -> random fallback", "e0c66a");
            _thoughtLog.Log(Personality.Name, "TOOL_CALL_FAILED", result.Error);
        }
        else
        {
            // Model genuinely unreachable, fallback mode allowed — still
            // a real, in-character line rather than dead air, just not
            // one that commits to actually doing anything.
            action = new GameAction("speak", "", 0f, message: "Sorry, give me a moment.");
            _uiLog($"[{Personality.Name}] mind unreachable during player request ({result.Error}) -> random fallback", "e0c66a");
            _thoughtLog.Log(Personality.Name, "FALLBACK", result.Error);
        }

        // Whatever this NPC was already committed to (following the
        // player, or anyone else — there's only ever one commitment at
        // a time today) ends here unless the fresh answer above happens
        // to renew that exact same one; see
        // UpdateFollowCommitmentAfterDecision's own comment. Answering a
        // brand-new request is exactly the kind of "changed their mind"
        // moment _followCommitmentTarget's header already calls out.
        UpdateFollowCommitmentAfterDecision(action);

        // Agreeing to walk with the player specifically is the one
        // response here that isn't a one-shot action — see
        // StartFollowCommitment's own header. Every other agreed action
        // (catch_fish, pick_apple, travel, trade, ...) just runs once,
        // the same as if it had come out of the normal full-menu turn.
        if (action.Id == "follow" && action.TargetId == playerName)
            StartFollowCommitment(playerName);

        string targetNote = action.TargetId != "" ? $" -> {action.TargetId}" : "";
        _uiLog($"[{Personality.Name}] attempting: {action.Id}{targetNote}", "d8ddd0");
        _thoughtLog.Log(Personality.Name, "ACTION_ATTEMPT", $"{action.Id}{targetNote}");
        Actor.AssignAction(action);
    }

    // Opens (or refreshes) the "walk with the player" commitment
    // TakeTurn() checks at the top of its normal decision each turn —
    // see _followCommitmentTarget's own header. Called only from
    // HandleDirectPlayerRequest's "agree" branch; there's no other way
    // into this state, and no code anywhere reads FollowCommitmentSeconds
    // besides this one line.
    private void StartFollowCommitment(string targetDisplayName)
    {
        _followCommitmentTarget = targetDisplayName;
        _followCommitmentEndMsec = Time.GetTicksMsec() + (ulong)(FollowCommitmentSeconds * 1000f);
        _followCommitmentTurnsSinceCheck = 0;
    }

    // Called after every genuine decision made while a commitment could
    // be live (the periodic recheck in TakeTurn(), and every answer out
    // of HandleDirectPlayerRequest) — the one place that actually ends a
    // commitment early over a real change of mind, rather than an
    // external interruption (danger, a fresh request) short-circuiting
    // it first. A no-op whenever there's nothing to end, or the fresh
    // decision just re-affirms the exact same follow.
    private void UpdateFollowCommitmentAfterDecision(GameAction action)
    {
        if (_followCommitmentTarget != null && !(action.Id == "follow" && action.TargetId == _followCommitmentTarget))
            _followCommitmentTarget = null;
    }

    // Guards the commitment above against the one edge case where it'd
    // otherwise force a follow at a target that no longer resolves to
    // anything real — the player disconnecting/despawning mid-session,
    // which doesn't normally happen today but costs nothing to guard.
    private bool FollowCommitmentTargetStillPresent()
    {
        foreach (IWorldCharacter agent in _world.Agents)
            if (agent.DisplayName == _followCommitmentTarget)
                return true;
        return false;
    }

    // Flagpoles are set up once in Main.BuildWorld() and never added or
    // removed afterward, so this id list is the same array, every
    // single call, for the entire session. Trees and fishing spots
    // WERE the same story until exploration-driven generation (see
    // WorldExploration/Main.GenerateContentAt) started adding more of
    // both as characters wander into new regions — so those two are
    // keyed off WorldContext.ContentVersion below and rebuild
    // themselves the first time they're asked for after it changes,
    // rather than being cached forever. Still real savings over
    // rebuilding every turn regardless (a handful of allocations x
    // however many NPCs x every turn, indefinitely) for what's still a
    // hot, repeating path, unlike NearbyNpcNames()/CarriedItems() below,
    // which really do change turn to turn and can't be cached this way.
    private string[] _treeIds;
    private int _treeIdsVersion = -1;
    private string[] TreeIds()
    {
        if (_treeIds == null || _treeIdsVersion != _world.ContentVersion)
        {
            _treeIds = new string[_world.Trees.Count];
            for (int i = 0; i < _treeIds.Length; i++)
                _treeIds[i] = $"tree_{i}";
            _treeIdsVersion = _world.ContentVersion;
        }
        return _treeIds;
    }

    private string[] _fishingSpotIds;
    private int _fishingSpotIdsVersion = -1;
    private string[] FishingSpotIds()
    {
        if (_fishingSpotIds == null || _fishingSpotIdsVersion != _world.ContentVersion)
        {
            _fishingSpotIds = new string[_world.FishingSpots.Count];
            for (int i = 0; i < _fishingSpotIds.Length; i++)
                _fishingSpotIds[i] = $"fish_{i}";
            _fishingSpotIdsVersion = _world.ContentVersion;
        }
        return _fishingSpotIds;
    }

    private string[] _pineTreeIds;
    private int _pineTreeIdsVersion = -1;
    private string[] PineTreeIds()
    {
        if (_pineTreeIds == null || _pineTreeIdsVersion != _world.ContentVersion)
        {
            _pineTreeIds = new string[_world.PineTrees.Count];
            for (int i = 0; i < _pineTreeIds.Length; i++)
                _pineTreeIds[i] = $"pine_{i}";
            _pineTreeIdsVersion = _world.ContentVersion;
        }
        return _pineTreeIds;
    }

    private string[] _berryBushIds;
    private int _berryBushIdsVersion = -1;
    private string[] BerryBushIds()
    {
        if (_berryBushIds == null || _berryBushIdsVersion != _world.ContentVersion)
        {
            _berryBushIds = new string[_world.BerryBushes.Count];
            for (int i = 0; i < _berryBushIds.Length; i++)
                _berryBushIds[i] = $"berry_{i}";
            _berryBushIdsVersion = _world.ContentVersion;
        }
        return _berryBushIds;
    }

    // NOT cached the way trees/bushes are — animals actually move
    // every frame (and can die at any time), so a list keyed off
    // ContentVersion alone would go stale the instant one wanders out
    // of range without anything having spawned or died. Same "recompute
    // every turn" treatment as NearbyNpcNames() below, for the same
    // reason.
    private string[] AnimalIds()
    {
        var ids = new List<string>();
        foreach (Animal a in _world.Animals)
            if (Actor.GlobalPosition.DistanceTo(a.GlobalPosition) <= SpeechLog.HearingRadius)
                ids.Add(a.WorldId);
        return ids.ToArray();
    }

    // --- fight / flee / freeze, and its lighter cousin, alert ---
    //
    // The whole thing lives here, not in NPCActor — NPCActor is the
    // tactical layer (it doesn't know about Animal or WorldContext at
    // all); these are mind-layer decisions like any other, just far
    // narrower and more urgent than TakeTurn()'s normal full-menu
    // path. Two distinct modes, per how dangerous things actually are
    // right now:
    //   - "danger" (DetectThreatSituation/HandleThreatTurn): a real
    //     fight is happening — this NPC is being attacked, or a nearby
    //     ally is. Genuinely serviced to the LLM first (narrowed to
    //     fight/flee/freeze), with an instant reflex default and a
    //     stat-weighted random fallback if the model can't be reached.
    //   - "alert" (DetectAlertAnimal/HandleAlert): a dangerous animal
    //     is simply nearby, not attacking anyone. Lighter-weight on
    //     purpose — a single dice roll against Intelligence/Bravery,
    //     no LLM round trip — since nothing urgent is actually
    //     happening yet; the sighting still gets folded into
    //     BuildPerception()'s own animal lines either way, so an LLM
    //     turn that ISN'T intercepted by either mode still sees it and
    //     can react on its own.
    // See TakeTurn() for where both get entered.

    // "Danger" is deliberately NOT Actor.UnderThreat (that's a rolling
    // window off the last landed HIT, built for CanSleep()'s narrower
    // "in fight mode" question). This is broader and more proactive:
    // an animal that's actively chasing or already swinging at this
    // NPC, or at a nearby ally, counts immediately — "a wolf is
    // attacking them" reads as the wolf's own behavior, not as "and it
    // already connected once." Self-targeting always wins over an
    // ally being targeted, if somehow both are true at once (can't
    // happen today — one animal has one TargetNode — but a future
    // multi-attacker scene shouldn't have to revisit this ordering).
    private class ThreatSituation
    {
        public List<Animal> Animals;
        public bool SelfTargeted;
    }

    private ThreatSituation DetectThreatSituation()
    {
        var selfAnimals = new List<Animal>();
        var allyAnimals = new List<Animal>();
        foreach (Animal a in _world.Animals)
        {
            if (!IsInstanceValid(a) || a.IsDown) continue;
            if (a.CurrentState != Animal.State.Chasing && a.CurrentState != Animal.State.Attacking) continue;

            if (ReferenceEquals(a.CurrentTarget, Actor))
                selfAnimals.Add(a);
            else if (a.CurrentTarget is NPCActor allyActor &&
                     Actor.GlobalPosition.DistanceTo(allyActor.GlobalPosition) <= SpeechLog.HearingRadius)
                allyAnimals.Add(a);
        }
        if (selfAnimals.Count > 0) return new ThreatSituation { Animals = selfAnimals, SelfTargeted = true };
        if (allyAnimals.Count > 0) return new ThreatSituation { Animals = allyAnimals, SelfTargeted = false };
        return null;
    }

    // Invalidates any FFF decision still in flight from an earlier
    // call to HandleThreatTurn() the moment a newer one starts — see
    // its own use below.
    private ulong _threatToken;

    private async Task HandleThreatTurn(ThreatSituation situation, string perception)
    {
        ulong myToken = ++_threatToken;
        List<Animal> threats = situation.Animals;
        bool selfTargeted = situation.SelfTargeted;

        // The instant reflex — fires now, before any round trip to the
        // model, so "a wolf just started coming for me" never leaves
        // this NPC just standing there while the real (comparatively
        // slow) decision is still in flight. "Choose default first
        // option like fight back, until LLM decides and get back with
        // another option to interrupt behavior" — AssignAction()
        // doesn't require the actor to be Idle first, so the real
        // decision below is free to overwrite this the moment it
        // arrives, exactly as asked for.
        GameAction reflex = DefaultReflexAction(threats, selfTargeted);
        _uiLog($"[{Personality.Name}] reflex: {reflex.Id}{(reflex.TargetId != "" ? $" -> {reflex.TargetId}" : "")}", "e0876b");
        _thoughtLog.Log(Personality.Name, "THREAT_REFLEX", reflex.Id);
        Actor.AssignAction(reflex);

        Mind.ThreatResult result = await Mind.DecideThreatResponse(perception, Personality, selfTargeted);
        _thinking = false;

        // Superseded by a newer threat turn (another hit landed, this
        // NPC moved on some other way) while this was still in
        // flight — a late-arriving decision from an earlier moment
        // shouldn't override whatever's actually happening now.
        if (myToken != _threatToken) return;
        if (Actor.IsDown) return;

        // The situation that started this could have resolved itself
        // by the time the decision comes back (threat dead, ally got
        // away, ...) — re-detect rather than trust what was captured
        // when this call started. It could also have shifted shape
        // (an ally-help scene turned into this NPC also getting
        // targeted, say) — whatever's true NOW is what the reflex
        // already responded to physically; re-deriving the WHOLE
        // decision over a shift in framing would be more churn than
        // it's worth, so this just uses the freshest target list and
        // keeps whichever choice the model/fallback already settled
        // on below.
        situation = DetectThreatSituation();
        if (situation == null) return;
        threats = situation.Animals;

        string choice;
        if (result.Ok)
        {
            choice = result.Choice;
            _uiLog($"[{Personality.Name}] decides (danger): {choice}", "a9c9e8");
            _thoughtLog.Log(Personality.Name, "THREAT_DECISION", choice);
        }
        else
        {
            // Always falls back here regardless of PureLlmMode (no
            // guard to check, unlike the two decision paths above) —
            // danger doesn't wait on a retry. Still worth telling apart
            // WHY, same as those two: a tool-call failure means the
            // model responded but didn't produce a usable choice.
            choice = RandomThreatFallback(threats, selfTargeted);
            string reason = IsToolCallFailure(result.Error) ? "LLM tool call failed" : "mind unreachable";
            _uiLog($"[{Personality.Name}] {reason} during danger ({result.Error}) -> random: {choice}", "e0c66a");
            _thoughtLog.Log(Personality.Name, "THREAT_FALLBACK", $"{result.Error} -> {choice}");
        }

        string finalChoice = MaybeInstinctOverride(choice, Actor.Stats.IntelligenceMod);
        finalChoice = MaybeCowardiceOverride(finalChoice, Actor.Stats.BraveryMod);
        if (finalChoice != choice)
        {
            _uiLog($"[{Personality.Name}] panics and {finalChoice}s instead", "e0876b");
            _thoughtLog.Log(Personality.Name, "THREAT_INSTINCT_OVERRIDE", $"{choice} -> {finalChoice}");
        }

        GameAction chosen = MapThreatChoiceToAction(finalChoice, threats);
        string targetNote = chosen.TargetId != "" ? $" -> {chosen.TargetId}" : "";
        _uiLog($"[{Personality.Name}] attempting: {chosen.Id}{targetNote}", "d8ddd0");
        _thoughtLog.Log(Personality.Name, "ACTION_ATTEMPT", $"{chosen.Id}{targetNote}");
        Actor.AssignAction(chosen);
    }

    // The reflexive default. Self-defense always fights back —
    // exactly as asked for ("choose default first option like fight
    // back") — not personality- or stat-aware on purpose: a genuine
    // self-preservation reflex fires before there's been any time to
    // weigh strength, odds, or temperament. Coming to a FRIEND's
    // defense is a different kind of reflex, though — wading into
    // someone else's fight without a beat of thought is itself a
    // bravery-driven instinct, not a survival one, so it's the one
    // reflex here that DOES read a stat: a merely average-or-braver
    // NPC's gut reaction is still to go help, but a genuinely fearful
    // one's isn't, and freezing (not fleeing) is the more honest
    // "hasn't decided yet" reflex for that case — the real decision
    // below, LLM or fallback, is what actually settles it.
    private GameAction DefaultReflexAction(List<Animal> threats, bool selfTargeted)
    {
        if (selfTargeted || Actor.Stats.BraveryMod >= 0)
        {
            Animal nearest = threats.OrderBy(a => Actor.GlobalPosition.DistanceTo(a.GlobalPosition)).First();
            return new GameAction("attack", nearest.WorldId, ActionRanges.Attack);
        }
        return new GameAction("wait", "", 0f);
    }

    // "If health is low or too many wolves, FLEE or FREEZE should be
    // weighed higher" — only reached when the mind itself couldn't be
    // reached at all (DecideThreatResponse failed outright), not a
    // general substitute for asking it. Bravery shifts the whole
    // distribution on top of that, for both self-defense and
    // ally-help alike — a genuinely brave NPC leans toward fighting
    // even by dice-roll default; a coward leans away from it even
    // when it's their own life on the line, not just a friend's.
    // Helping a friend also starts from a slightly more cautious
    // baseline than self-defense before Bravery adjusts it — jumping
    // into someone else's fight is a choice in a way defending
    // yourself isn't.
    private string RandomThreatFallback(List<Animal> threats, bool selfTargeted)
    {
        int weightFight = 3, weightFlee = 3, weightFreeze = 2;
        if (Actor.Vitals.Health < 40f) { weightFight -= 2; weightFlee += 2; weightFreeze += 1; }
        if (threats.Count > 1) { weightFight -= 1; weightFlee += 2; }
        if (!selfTargeted) { weightFight -= 1; weightFreeze += 1; }

        int braveryMod = Actor.Stats.BraveryMod;
        weightFight += braveryMod;
        weightFlee -= braveryMod / 2;

        weightFight = Math.Max(1, weightFight);
        weightFlee = Math.Max(1, weightFlee);
        weightFreeze = Math.Max(1, weightFreeze);

        int total = weightFight + weightFlee + weightFreeze;
        int roll = Rng.Next(total);
        if (roll < weightFight) return "fight";
        if (roll < weightFight + weightFlee) return "flee";
        return "freeze";
    }

    // "Bad choices" from low Intelligence — a chance, inversely tied
    // to IntelligenceMod, that instinct overrides whatever was
    // actually decided (by the LLM OR the random fallback above) with
    // something worse. A sharp character (+4ish) almost never panics
    // this way; a dull one (-4ish) does more than half the time.
    // Deliberately never a NO-OP roll (picking the same choice again
    // would just be indistinguishable from not overriding at all).
    private string MaybeInstinctOverride(string choice, int intelligenceMod)
    {
        float overrideChance = Mathf.Clamp(0.3f - intelligenceMod * 0.07f, 0.05f, 0.55f);
        if (Rng.NextDouble() >= overrideChance) return choice;

        string[] alternatives = choice switch
        {
            "fight" => new[] { "freeze" },
            "flee" => new[] { "fight", "freeze" },
            _ => new[] { "fight" }, // freeze overridden by a panicked lunge — the classic bad instinct
        };
        return alternatives[Rng.Next(alternatives.Length)];
    }

    // A separate roll from MaybeInstinctOverride above — this one's
    // about nerve, not sense, and only ever downgrades a "fight"
    // choice (fleeing or freezing was already the cautious call, there's
    // nothing braver to chicken out INTO). A low-Bravery NPC can talk
    // itself (or be talked, by the LLM) into fighting and still lose
    // its nerve in the moment; a genuinely brave one almost never does.
    private string MaybeCowardiceOverride(string choice, int braveryMod)
    {
        if (choice != "fight") return choice;
        float chickenChance = Mathf.Clamp(0.35f - braveryMod * 0.08f, 0.03f, 0.7f);
        if (Rng.NextDouble() >= chickenChance) return choice;
        return Rng.NextDouble() < 0.5 ? "flee" : "freeze";
    }

    private GameAction MapThreatChoiceToAction(string choice, List<Animal> threats)
    {
        switch (choice)
        {
            case "fight":
                Animal nearest = threats.OrderBy(a => Actor.GlobalPosition.DistanceTo(a.GlobalPosition)).First();
                return new GameAction("attack", nearest.WorldId, ActionRanges.Attack);
            case "flee":
                return new GameAction("flee", "", 0f, destination: ComputeFleeDestination(threats));
            default: // "freeze"
                return new GameAction("wait", "", 0f);
        }
    }

    // A point roughly opposite the average direction of every current
    // threat, far enough to actually put distance between them, kept
    // inside the generated map — no point fleeing toward a boundary
    // that doesn't actually help.
    private Vector2 ComputeFleeDestination(List<Animal> threats)
    {
        Vector2 avgThreatPos = Vector2.Zero;
        foreach (Animal a in threats) avgThreatPos += a.GlobalPosition;
        avgThreatPos /= threats.Count;

        Vector2 away = Actor.GlobalPosition - avgThreatPos;
        if (away.LengthSquared() < 1f) away = new Vector2(1f, 0f); // degenerate: standing right on top of the threat — any direction beats dividing by ~zero
        away = away.Normalized();

        Vector2 dest = Actor.GlobalPosition + away * 320f;
        Rect2 bounds = WorldExploration.MaxMapBounds;
        dest.X = Mathf.Clamp(dest.X, bounds.Position.X + 40f, bounds.Position.X + bounds.Size.X - 40f);
        dest.Y = Mathf.Clamp(dest.Y, bounds.Position.Y + 40f, bounds.Position.Y + bounds.Size.Y - 40f);
        return dest;
    }

    // --- alert: a dangerous animal is nearby, but nobody's fighting ---
    //
    // Deliberately mechanical, no LLM call — see this section's own
    // header above for why. One roll per NEW sighting, not one every
    // turn the same animal happens to still be around: _lastAlertAnimalId
    // remembers which one this NPC already reacted to so it doesn't
    // re-roll (and re-flee, or re-charge) every few seconds against
    // the same wolf just standing there. Reset the moment nothing
    // alert-worthy is in range at all, so a later sighting — of that
    // same animal, or a different one — gets a fresh roll.
    private string _lastAlertAnimalId;

    private Animal DetectAlertAnimal()
    {
        Animal nearest = null;
        float bestDist = float.MaxValue;
        foreach (Animal a in _world.Animals)
        {
            if (!IsInstanceValid(a) || a.IsDown) continue;
            if (a is not (Wolf or Bear)) continue; // rabbits are never alert-worthy — see BuildPerception's own "harmless" framing
            float d = Actor.GlobalPosition.DistanceTo(a.GlobalPosition);
            if (d <= SpeechLog.HearingRadius && d < bestDist) { bestDist = d; nearest = a; }
        }
        return nearest;
    }

    // Returns the mechanical reaction (to assign immediately, skipping
    // this turn's normal full-menu decision), or null if this NPC's
    // roll came up "doesn't register it as a threat" — in which case
    // TakeTurn() falls through to the normal turn, where
    // BuildPerception()'s own animal line still honestly names the
    // animal as "worth being careful around," and the model gets to
    // react (or not) on its own.
    private GameAction HandleAlert(Animal dangerous)
    {
        if (dangerous.WorldId == _lastAlertAnimalId)
            return null; // already rolled for this one — see this section's header
        _lastAlertAnimalId = dangerous.WorldId;

        string species = dangerous is Bear ? "bear" : "wolf";
        int intMod = Actor.Stats.IntelligenceMod;
        int braveryMod = Actor.Stats.BraveryMod;

        // "Just dumb and doesn't think wolves are a danger" — doesn't
        // register this as a threat at all, so nothing here overrides
        // its normal turn.
        float obliviousChance = Mathf.Clamp(0.25f - intMod * 0.06f, 0.05f, 0.5f);
        if (Rng.NextDouble() < obliviousChance)
        {
            _thoughtLog.Log(Personality.Name, "ALERT", $"didn't register the {species} nearby as a threat");
            return null;
        }

        // "Brave and dumb and wants to fight the wolf" — recognizes
        // the danger and goes looking for it anyway.
        float boldChance = Mathf.Clamp(0.15f + braveryMod * 0.08f, 0.02f, 0.6f);
        if (Rng.NextDouble() < boldChance)
        {
            _uiLog($"[{Personality.Name}] spots a {species} and goes after it", "e0876b");
            _thoughtLog.Log(Personality.Name, "ALERT", $"bold — approaches the {species} ({dangerous.WorldId})");
            return new GameAction("attack", dangerous.WorldId, ActionRanges.Attack);
        }

        // The default: "npcs should move away from the wolf typically."
        _uiLog($"[{Personality.Name}] spots a {species} and moves away from it", "e0c66a");
        _thoughtLog.Log(Personality.Name, "ALERT", $"cautious — moves away from the {species} ({dangerous.WorldId})");
        return new GameAction("flee", "", 0f, destination: ComputeFleeDestination(new List<Animal> { dangerous }));
    }

    private string[] _stickIds;
    private int _stickIdsVersion = -1;
    private string[] StickIds()
    {
        if (_stickIds == null || _stickIdsVersion != _world.ContentVersion)
        {
            _stickIds = new string[_world.Sticks.Count];
            for (int i = 0; i < _stickIds.Length; i++)
                _stickIds[i] = _world.Sticks[i].WorldId;
            _stickIdsVersion = _world.ContentVersion;
        }
        return _stickIds;
    }

    private string[] _travelTargetIds;
    private string[] TravelTargetIds()
    {
        if (_travelTargetIds == null)
        {
            _travelTargetIds = new string[_world.Flagpoles.Count];
            _world.Flagpoles.Keys.CopyTo(_travelTargetIds, 0);
        }
        return _travelTargetIds;
    }

    // Names of other NPCs currently within hearing/perception range —
    // the enum for "follow"'s target. Deliberately restricted to who's
    // actually nearby right now, same anti-hallucination posture as
    // every other target list: you can't choose to follow someone you
    // have no way of knowing is around.
    private string[] NearbyNpcNames()
    {
        var names = new List<string>();
        foreach (IWorldCharacter other in _world.Agents)
        {
            if (other.Id == Id)
                continue;
            if (Actor.GlobalPosition.DistanceTo(other.GlobalPosition) <= SpeechLog.HearingRadius)
                names.Add(other.DisplayName);
        }
        return names.ToArray();
    }

    // Charisma actually mattering mechanically, without a "persuade"
    // action forcing an outcome — there's deliberately no such action
    // (see Mind's own header comment for why). Instead, every heard
    // line gets one opposed roll folded in as plain, qualitative
    // framing — the same shape of check steal already uses (see
    // SkillCheck/DifficultyClass.OpposedBase), just never gating
    // anything itself. This NPC's own Mind still decides, next turn,
    // whether to actually go along with what it heard — a convincing
    // roll doesn't force compliance and an unconvincing one doesn't
    // forbid it, it's just one more honest piece of context alongside
    // personality, relationship, and everything else already in play.
    private string DescribePersuasionHint(string speakerName)
    {
        int? speakerCharisma = FindCharismaMod(speakerName);
        if (speakerCharisma == null)
            return ""; // speaker's gone, or something else looked up their name wrong — say nothing rather than guess

        // A direct ask from the real player carries a little extra
        // weight on top of raw Charisma — "most of them should
        // actually follow me... they're too independent right now."
        // Not an automatic win (a genuinely low-Charisma ask can still
        // fail to land, and this is still only a hint feeding the
        // NPC's own reasoning, never a compliance guarantee — see
        // Mind.cs's own header on why there's deliberately no
        // "persuade" action that forces anything), just a real, honest
        // thumb on the scale: someone who sought you out specifically
        // to ask is inherently a bit more persuasive than the same
        // words from a stranger.
        int bonus = IsPlayerName(speakerName) ? 4 : 0;
        var check = SkillCheck.Roll(speakerCharisma.Value + bonus, DifficultyClass.OpposedBase + Actor.Stats.CharismaMod);
        return check.Success
            ? " (this comes across as pretty convincing to you)"
            : " (this doesn't really land for you — easy to brush off if you're not already inclined to agree)";
    }

    private int? FindCharismaMod(string displayName)
    {
        foreach (IWorldCharacter agent in _world.Agents)
            if (agent.DisplayName == displayName)
                return WorldContext.ActorOf(agent)?.Stats.CharismaMod;
        return null;
    }

    private bool IsPlayerName(string displayName)
    {
        foreach (IWorldCharacter agent in _world.Agents)
            if (agent.DisplayName == displayName)
                return agent is PlayerCharacter;
        return false;
    }

    // What this NPC actually has on hand right now — the enum for
    // trade's "item" argument, so it can only ever offer something it
    // really has (steal's enum is fixed to every item type that exists
    // in the world instead, since it's a guess about someone ELSE's
    // inventory, not a statement about your own).
    private string[] CarriedItems()
    {
        var items = new List<string>(Actor.Inventory.All.Count);
        foreach (string item in Actor.Inventory.All.Keys)
            items.Add(item);
        return items.ToArray();
    }

    // Every landmark in the world, resources and flagpoles alike — feeds
    // SpatialMemory.Observe() each turn. Resources are tracked here too
    // even though their perception text doesn't currently branch on
    // "known" status (an NPC starting in the village already knows where
    // the trees are) — the geo-map should still be a complete, honest
    // record of where this NPC has actually been.
    private IEnumerable<(string Id, Vector2 Position)> AllLandmarks()
    {
        for (int i = 0; i < _world.Trees.Count; i++)
            yield return ($"tree_{i}", _world.Trees[i].GlobalPosition);
        for (int i = 0; i < _world.FishingSpots.Count; i++)
            yield return ($"fish_{i}", _world.FishingSpots[i].GlobalPosition);
        for (int i = 0; i < _world.PineTrees.Count; i++)
            yield return ($"pine_{i}", _world.PineTrees[i].GlobalPosition);
        for (int i = 0; i < _world.BerryBushes.Count; i++)
            yield return ($"berry_{i}", _world.BerryBushes[i].GlobalPosition);
        // Animals deliberately excluded — SpatialMemory is about
        // static locations ("have I been near X before"), and an
        // animal never sits still long enough for that to mean
        // anything.
        foreach (Stick s in _world.Sticks)
            yield return (s.WorldId, s.GlobalPosition);
        yield return ("home", _world.Home.GlobalPosition);
        foreach (KeyValuePair<string, Node2D> kv in _world.Flagpoles)
            yield return (kv.Key, kv.Value.GlobalPosition);
    }

    // Shared by BuildPerception() for all four resource types (trees,
    // fishing spots, pine trees, berry bushes) — same sort-take-
    // describe shape each needs, just a different id prefix and a
    // different way to read "how much is left" (AppleTree/FishingSpot/
    // GatherableFoliage don't share an interface for that, so a lambda
    // reads it instead of forcing one in just for this).
    private const int NearbyResourceCount = 6;
    private void AppendNearestResources<T>(List<string> lines, List<T> items, string idPrefix, System.Func<T, int> remaining, string itemPlural) where T : Node2D
    {
        List<(T Item, int Index)> nearest = items
            .Select((item, i) => (item, i))
            .OrderBy(pair => Actor.GlobalPosition.DistanceTo(pair.item.GlobalPosition))
            .Take(NearbyResourceCount)
            .ToList();
        foreach ((T item, int i) in nearest)
        {
            int dist = (int)Actor.GlobalPosition.DistanceTo(item.GlobalPosition);
            lines.Add($"{idPrefix}_{i}: {remaining(item)} {itemPlural} left, {dist} px away");
        }
    }

    private string BuildPerception(List<string> freshHeard, List<string> freshWitnessed)
    {
        var lines = new List<string> { Personality.DescribeForPrompt() };
        if (_lastResultLine != "")
            lines.Add(_lastResultLine);
        // Right up front, not buried in Memory's chronological dump —
        // see the caller's own comments on freshHeard/freshWitnessed
        // for why both needed their own prominent spot.
        lines.AddRange(freshHeard);
        lines.AddRange(freshWitnessed);

        // Nearest-N, not "every one that exists" — fine when the world
        // was a fixed 3 trees + 2 fishing spots, but exploration-driven
        // generation (see WorldExploration) can grow any of these lists
        // indefinitely (up to WorldExploration.MaxMapBounds) as
        // characters wander. Capping to what's actually close keeps the
        // prompt bounded regardless of how much of the map has been
        // uncovered, and "what's nearby" is what a decision about
        // gathering actually needs anyway — a tree three regions away
        // isn't a real option this turn. One shared helper (below) for
        // all four resource lists rather than four copies of the same
        // sort-take-describe dance.
        AppendNearestResources(lines, _world.Trees, "tree", t => t.AppleCount, "apples");
        AppendNearestResources(lines, _world.FishingSpots, "fish", f => f.FishCount, "fish");
        AppendNearestResources(lines, _world.PineTrees, "pine", p => p.Count, "pinecones");
        AppendNearestResources(lines, _world.BerryBushes, "berry", b => b.Count, "berries");

        int homeDist = (int)Actor.GlobalPosition.DistanceTo(_world.Home.GlobalPosition);
        lines.Add($"home: {homeDist} px away, {_world.Home.ApplesStored} apples and {_world.Home.FishStored} fish stored there so far");

        int firePitDist = (int)Actor.GlobalPosition.DistanceTo(_world.FirePit.GlobalPosition);
        lines.Add(_world.FirePit.IsLit
            ? $"fire pit: {firePitDist} px away, near home, burning right now — a stick can be lit from it to make a torch, and raw rabbit meat can be cooked over it."
            : $"fire pit: {firePitDist} px away, near home, not lit right now.");

        // Known vs heuristic-only: a flagpole the NPC has actually
        // reached before gets described plainly; one it's only ever
        // seen from a distance is framed as vague and undirected —
        // there's no known path, only roughly how far away it is.
        foreach (KeyValuePair<string, Node2D> kv in _world.Flagpoles)
        {
            string id = kv.Key;
            int dist = (int)Actor.GlobalPosition.DistanceTo(kv.Value.GlobalPosition);
            lines.Add(Spatial.Knows(id)
                ? $"{id}: a place you've actually been before, {dist} px away."
                : $"{id}: a hazy, distant landmark you've only ever seen from afar — you don't know a real path there, only that it's roughly {dist} px away.");
        }

        // Who's actually around right now, by name — talking, hearing,
        // and follow all only work within SpeechLog.HearingRadius, and
        // knowing WHO (not just that someone) is nearby is what makes
        // "declare something and see who's around" or "follow Wren" a
        // real, groundable decision rather than a guess.
        foreach (IWorldCharacter other in _world.Agents)
        {
            if (other.Id == Id)
                continue;
            int dist = (int)Actor.GlobalPosition.DistanceTo(other.GlobalPosition);
            if (dist <= SpeechLog.HearingRadius)
                lines.Add($"{other.DisplayName} is nearby, {dist} px away, feeling {other.CurrentEmotion.ToWireString()}.");
        }

        // Wild animals — same hearing-range scoping as everyone above.
        // Framed with enough to actually judge the situation (species,
        // distance, whether it's actively coming for YOU or someone
        // else specifically) without exposing raw internal numbers
        // (Hunger%, Health) that would just be noise to reason about —
        // "it looks hostile" is the actionable fact, not the number
        // behind it. A wolf/bear that ISN'T attacking anyone right now
        // still gets an explicit "worth being careful around" note —
        // this is what feeds NpcAgent's own is_alert handling (see
        // DetectAlertAnimal/HandleAlert) into the LLM's normal turn
        // too, not just this NPC's own mechanical reflex to it.
        foreach (Animal a in _world.Animals)
        {
            int dist = (int)Actor.GlobalPosition.DistanceTo(a.GlobalPosition);
            if (dist > SpeechLog.HearingRadius)
                continue;
            string species = a switch { Wolf => "wolf", Bear => "bear", Rabbit => "rabbit", _ => "animal" };
            bool hostileNow = a.CurrentState == Animal.State.Attacking || a.CurrentState == Animal.State.Chasing;
            string note;
            if (species == "rabbit")
                note = " — harmless, just foraging.";
            else if (hostileNow && a.CurrentTarget == Actor)
                note = " — it's coming for YOU, right now!";
            else if (hostileNow && a.CurrentTarget is NPCActor victimActor)
                note = $" — it's attacking {_world.NameOf(victimActor) ?? "someone nearby"} right now!";
            else
                note = " — a dangerous animal, not attacking anyone right now, but worth being careful around.";
            lines.Add($"{a.WorldId} ({species}): {dist} px away{note}");
        }

        // Sticks on the ground — NOT AppendNearestResources (that
        // reconstructs "{prefix}_{list index}", which breaks here the
        // same way it would for animals: a stick can be picked up from
        // the middle of the list, shifting every later index. Sticks
        // are few enough that "every one within range" (not just
        // nearest-N) is fine to list in full.
        foreach (Stick s in _world.Sticks)
        {
            int dist = (int)Actor.GlobalPosition.DistanceTo(s.GlobalPosition);
            if (dist <= SpeechLog.HearingRadius)
                lines.Add($"{s.WorldId} (stick): {dist} px away, lying on the ground.");
        }

        lines.Add($"You are carrying: {Actor.Inventory.Describe()}.");
        lines.Add($"You are currently feeling {Actor.CurrentEmotion.ToWireString()}.");
        // Full self-awareness of your own stats and condition — same
        // numbers every check against you actually uses, not a hint or
        // a summary. Nothing reads this and forces a decision (no code
        // anywhere blocks travel/gathering on low fatigue) — it's
        // information for you to reason about like anything else here.
        lines.Add($"Your natural abilities: {Actor.Stats.Describe()}.");
        lines.Add($"Your physical condition: {Actor.Vitals.Describe()}.");

        return $"{string.Join("\n", lines)}\n\n{Memory.Render()}";
    }

    // Named-landmark description for the memory trail — raw coordinates
    // aren't meaningful to read back later, "near tree_1" is.
    private string DescribeLocation()
    {
        string nearest = "home";
        float bestDist = Actor.GlobalPosition.DistanceTo(_world.Home.GlobalPosition);
        for (int i = 0; i < _world.Trees.Count; i++)
        {
            float d = Actor.GlobalPosition.DistanceTo(_world.Trees[i].GlobalPosition);
            if (d < bestDist) { bestDist = d; nearest = $"tree_{i}"; }
        }
        for (int i = 0; i < _world.FishingSpots.Count; i++)
        {
            float d = Actor.GlobalPosition.DistanceTo(_world.FishingSpots[i].GlobalPosition);
            if (d < bestDist) { bestDist = d; nearest = $"fish_{i}"; }
        }
        for (int i = 0; i < _world.PineTrees.Count; i++)
        {
            float d = Actor.GlobalPosition.DistanceTo(_world.PineTrees[i].GlobalPosition);
            if (d < bestDist) { bestDist = d; nearest = $"pine_{i}"; }
        }
        for (int i = 0; i < _world.BerryBushes.Count; i++)
        {
            float d = Actor.GlobalPosition.DistanceTo(_world.BerryBushes[i].GlobalPosition);
            if (d < bestDist) { bestDist = d; nearest = $"berry_{i}"; }
        }
        foreach (KeyValuePair<string, Node2D> kv in _world.Flagpoles)
        {
            float d = Actor.GlobalPosition.DistanceTo(kv.Value.GlobalPosition);
            if (d < bestDist) { bestDist = d; nearest = kv.Key; }
        }
        return $"near {nearest} ({(int)bestDist}px away)";
    }

    // True for a "the model responded, but never produced a usable
    // tool call this attempt" failure (Mind.ParseToolCall's own
    // no_tool_call / malformed_tool_call / unknown_action_X /
    // invalid_target_X) — as opposed to a genuine think_/act_-prefixed
    // network/provider failure (the backend itself never answered; see
    // Mind.Decide/DecidePlayerRequest/DecideThreatResponse, which are
    // the only things that ever produce either prefix). The distinction
    // is what "allow fallback if the LLM tool call fails, but tell me
    // why" actually means: under PureLlmMode a real connectivity
    // failure still retries forever below (there's nothing anywhere
    // else to substitute for a decision the model never got the chance
    // to make), but a tool-call failure is a per-attempt quality issue
    // on a backend that's demonstrably still reachable — falling back
    // and clearly labeling it keeps that one NPC's turn loop moving
    // instead of stalling it indefinitely on a live connection.
    private static bool IsToolCallFailure(string error) =>
        error != null && !error.StartsWith("think_") && !error.StartsWith("act_");

    // Used only when the mind is unreachable OR fails to produce a
    // usable tool call — not personality-aware on purpose, since a
    // fallback isn't a real decision. Rather than a fixed priority
    // order, it picks randomly among whatever's actually valid right
    // now (never a nonsensical option, like depositing nothing or
    // picking an empty tree). Deliberately never picks speak/travel/
    // follow/trade/steal — this isn't a social or exploratory impulse,
    // it's a fallback for the core resource loop.
    private GameAction RandomFallback()
    {
        // A physical need, not a social or exploratory impulse — checked
        // even before deposit, same reasoning as everything else in this
        // fallback: basic self-maintenance still makes sense with no
        // mind reachable to actually decide anything.
        if (Actor.Vitals.NeedsSleep)
            return new GameAction("sleep", "", 0f);

        // Same reasoning as sleep above — eating below 80% Health (or
        // being genuinely hungry) is a physical need, not a choice, so
        // it belongs ahead of deposit here too. CanEat() already checks
        // both those thresholds and that there's actually food in
        // Inventory — this only catches the case CanEat() can't offer
        // at all: the need is real, but there's nothing edible on hand
        // yet to eat.
        if (Actor.CanEat())
            return new GameAction("eat", "", 0f);

        var foodOptions = new List<GameAction>();
        for (int i = 0; i < _world.Trees.Count; i++)
            if (_world.Trees[i].AppleCount > 0)
                foodOptions.Add(new GameAction("pick_apple", $"tree_{i}", ActionRanges.PickApple));
        for (int i = 0; i < _world.FishingSpots.Count; i++)
            if (_world.FishingSpots[i].FishCount > 0)
                foodOptions.Add(new GameAction("catch_fish", $"fish_{i}", ActionRanges.CatchFish));
        for (int i = 0; i < _world.BerryBushes.Count; i++)
            if (_world.BerryBushes[i].Count > 0)
                foodOptions.Add(new GameAction("gather_berry", $"berry_{i}", ActionRanges.GatherBerry));

        // Genuinely needs food and has none — gathering something
        // edible takes priority even over depositing whatever's
        // already in the pack (a pinecone can wait; a badly hurt or
        // starving body can't). Without this, the plain random pick
        // below would treat pinecones (not food at all) as an equally
        // likely choice as an apple/fish/berry even while starving,
        // and would walk home to deposit a single pinecone before ever
        // considering food at all.
        bool urgentlyNeedsFood = (Actor.Vitals.Health < NPCActor.EatHealthThreshold || Actor.Vitals.NeedsFood)
            && Food.BestFoodIn(Actor.Inventory) == null;
        if (urgentlyNeedsFood && foodOptions.Count > 0)
            return foodOptions[Rng.Next(foodOptions.Count)];

        if (Actor.Inventory.All.Count > 0)
            return new GameAction("deposit", "home", ActionRanges.Deposit);

        var options = new List<GameAction>(foodOptions);
        for (int i = 0; i < _world.PineTrees.Count; i++)
            if (_world.PineTrees[i].Count > 0)
                options.Add(new GameAction("gather_pinecone", $"pine_{i}", ActionRanges.GatherPinecone));

        if (options.Count == 0)
            return new GameAction("wait", "", 0f);

        return options[Rng.Next(options.Count)];
    }

    // Reasons that mean "bad luck," not "wrong choice" — a fumbled gather
    // or a failed steal are real dice rolls that could go the other way
    // next time on the exact same target. Conflating these with a
    // structural block (depleted,
    // hands full, target gone — where repeating really is pointless
    // until something changes) was actively steering NPCs away from a
    // perfectly good tree after two unlucky rolls in a row.
    private static readonly HashSet<string> ChanceBasedReasons = new() { "fumbled", "steal_failed" };

    // Tracks whether the SAME action+target+reason just failed again, and
    // escalates the message accordingly — the log showed a small model
    // will happily retry an identical failing action a dozen times
    // unless the fact that it's repeating is made explicit and blunt.
    // Structural failures escalate fast and say "stop, change approach";
    // chance-based ones escalate slower and say "that's just luck."
    private void UpdateLastResult(bool success, string actionId, string targetId, string reason)
    {
        string what = targetId != "" ? $"{actionId} on {targetId}" : actionId;

        if (success)
        {
            _consecutiveFailures = 0;
            _lastFailureKey = "";
            _lastResultLine = $"Your last action ({what}) succeeded.";
            return;
        }

        string failureKey = $"{actionId}|{targetId}|{reason}";
        _consecutiveFailures = (failureKey == _lastFailureKey) ? _consecutiveFailures + 1 : 1;
        _lastFailureKey = failureKey;
        bool chanceBased = ChanceBasedReasons.Contains(reason);

        if (chanceBased)
        {
            _lastResultLine = _consecutiveFailures >= 3
                ? $"Your last action ({what}) FAILED again ({reason}) — that's {_consecutiveFailures} times in a row, but each one was just bad luck on a roll, not a sign the choice itself is wrong. Trying again is still reasonable; a change of pace is fine too."
                : $"Your last action ({what}) FAILED ({reason}) — bad luck on the roll, not a real block. Trying again is perfectly reasonable.";
            if (_consecutiveFailures >= 4)
                Memory.Record("action", $"Rough luck streak: {what} has failed {_consecutiveFailures} times in a row ({reason}), all chance-based, not structural.");
            return;
        }

        _lastResultLine = _consecutiveFailures >= 2
            ? $"Your last action ({what}) FAILED again ({reason}) — that's {_consecutiveFailures} times in a row with the same result. Stop repeating it; do something that actually addresses why it keeps failing."
            : $"Your last action ({what}) FAILED ({reason}).";

        if (_consecutiveFailures >= 2)
            Memory.Record("action", $"REPEATED FAILURE: {what} has now failed {_consecutiveFailures} times in a row ({reason}).");
    }

    // See InventoryWatcher for the actual comparison; this just decides
    // what an NPC specifically does with each note it returns. A loss
    // gets a console line too (worth a human watching noticing); a gain
    // only goes to Memory/the thought log — the NPC itself still reasons
    // about it, it just doesn't print as its own event. Distinguishing
    // the two from the note text ("missing" vs "more") is a little
    // fragile, but confined to this one call site rather than something
    // the shared class needs to know about NpcAgent's specific logging.
    private void NoticeInventoryChanges()
    {
        foreach (string note in _inventoryWatcher.DetectExternalChanges(Actor.Inventory))
        {
            bool isLoss = note.Contains("missing");
            if (isLoss)
                _uiLog($"[{Personality.Name}] {note}", "e0876b");
            Memory.Record("inventory", note);
            _thoughtLog.Log(Personality.Name, isLoss ? "INVENTORY_LOSS_NOTICED" : "INVENTORY_GAIN_NOTICED", note);
        }
    }

    // Feeds WorldEventLog so nearby characters can notice this NPC's
    // own non-secret, non-speech actions on their own next turn — see
    // RecentEventBuffer's header for why batching it this way is what
    // keeps this from turning into a flood. Only called for actions
    // OTHER than trade/steal (see OnActionCompleted below) — trade
    // announces itself right where its "given_to" detail already lives,
    // and steal deliberately never goes through here at all (see
    // AnnounceStealthAttempt instead). wait and speak fall through to
    // the default on purpose: doing nothing isn't a notable event, and
    // speech already has its own, audible channel (SpeechLog).
    private void AnnounceVisibleAction(string actionId, string targetId, Godot.Collections.Dictionary data)
    {
        string description = actionId switch
        {
            "pick_apple" => $"{Personality.Name} picks an apple from a tree.",
            "catch_fish" => $"{Personality.Name} catches a fish.",
            "gather_pinecone" => $"{Personality.Name} gathers a pinecone from a pine tree.",
            "gather_berry" => $"{Personality.Name} picks a berry from a bush.",
            "deposit" => $"{Personality.Name} deposits their haul at home.",
            "travel" => $"{Personality.Name} arrives at {targetId}.",
            "follow" => $"{Personality.Name} walks up alongside {targetId}.",
            "attack" => $"{Personality.Name} strikes {targetId}!",
            "eat" => $"{Personality.Name} eats something to recover.",
            "pick_up_stick" => $"{Personality.Name} picks up a stick.",
            "sleep" => $"{Personality.Name} was asleep nearby for a while.",
            "flee" => $"{Personality.Name} flees in a panic!",
            "light_fire" => $"{Personality.Name} lights the fire pit.",
            "make_torch" => $"{Personality.Name} lights a torch from the fire.",
            "cook_meat" => $"{Personality.Name} cooks some meat over the fire.",
            _ => null,
        };
        if (description != null)
            WorldEventLog.Announce(Personality.Name, Actor.GlobalPosition, description);
    }

    private async void OnActionCompleted(Godot.Collections.Dictionary result)
    {
        bool success = result["success"].AsBool();
        string actionId = result["action"].AsString();
        string targetId = result["target_id"].AsString();
        string message = result["message"].AsString();
        string reason = result["reason"].AsString();
        var data = result["data"].AsGodotDictionary();
        string rollSummary = SkillCheck.SummarizeData(data);
        string color = success ? "8fd694" : "e0876b";
        _uiLog($"[{Personality.Name}] -> {actionId}: {(success ? "OK" : "FAILED")} ({reason}){rollSummary}", color);
        Memory.Record("action", $"{actionId} {(success ? "succeeded" : $"failed ({reason})")}{rollSummary}");
        _thoughtLog.Log(Personality.Name, "ACTION_RESULT", $"{actionId} {(success ? "OK" : "FAILED")} ({reason}){rollSummary}");

        if (success && actionId == "speak")
        {
            SpeechLog.Say(Personality.Name, Actor.GlobalPosition, message);
            Actor.ShowSpeechBubble();
            _uiLog($"[{Personality.Name}] says: \"{message}\"", "e8d9a9");
            Memory.Record("speech", $"Said: \"{message}\"");
            _thoughtLog.Log(Personality.Name, "SPEAK", message);
        }

        if (success && (actionId == "trade" || actionId == "steal"))
        {
            string item = data["item"].AsString();
            int amount = data["amount"].AsInt32();
            if (actionId == "trade")
            {
                string givenTo = data["given_to"].AsString();
                _uiLog($"[{Personality.Name}] gives {amount} {item}(s) to {givenTo}", "e8d9a9");
                Memory.Record("trade", $"Gave {amount} {item}(s) to {givenTo}.");
                _thoughtLog.Log(Personality.Name, "TRADE", $"gave {amount} {item} to {givenTo}");
                // A real, public exchange — unlike steal below, nothing
                // about this is hidden, so it goes through the normal
                // broadcast every nearby character can see.
                WorldEventLog.Announce(Personality.Name, Actor.GlobalPosition, $"{Personality.Name} gives {amount} {item}(s) to {givenTo}.");
            }
            else
            {
                string stolenFrom = data["stolen_from"].AsString();
                // Not written to Memory as "I stole" being announced
                // anywhere — the victim finds out (or doesn't) purely
                // through NoticeInventoryChanges() on their own side,
                // never a direct notification. This line and the log
                // below are the human-watching-the-console view only.
                _uiLog($"[{Personality.Name}] takes {amount} {item}(s) from {stolenFrom} without asking{rollSummary}", "e0876b");
                Memory.Record("steal", $"Took {amount} {item}(s) from {stolenFrom} without asking.");
                _thoughtLog.Log(Personality.Name, "STEAL", $"stole {amount} {item} from {stolenFrom}");

                // The one action's own path into WorldEventLog, apart
                // from AnnounceVisibleAction() below — everyone else
                // nearby genuinely never gets a chance at this one,
                // only whoever's own Wisdom roll beats this NPC's
                // Dexterity (see AnnounceStealthAttempt's own comment).
                WorldEventLog.AnnounceStealthAttempt(
                    Personality.Name, stolenFrom, Actor.GlobalPosition,
                    $"You notice {Personality.Name} take something from {stolenFrom} without asking.",
                    Actor.Stats.DexterityMod, _world.CharactersWithStats());
            }
        }
        else if (success)
        {
            AnnounceVisibleAction(actionId, targetId, data);
        }

        UpdateLastResult(success, actionId, targetId, reason);

        // Absorb THIS action's own effect on inventory into the baseline
        // now, before TakeTurn() runs again below — see
        // InventoryWatcher.AbsorbOwnChange()'s comment for why the
        // ordering matters.
        _inventoryWatcher.AbsorbOwnChange(Actor.Inventory);

        if (Memory.NeedsCompression)
        {
            _uiLog($"[{Personality.Name}] ...compressing memory...", "6f8068");
            await Memory.CompressIfNeeded(Mind);
            _uiLog($"[{Personality.Name}] diary: {Memory.Diary}", "a9c9e8");
            _thoughtLog.Log(Personality.Name, "MEMORY_COMPRESSED", Memory.Diary);
        }

        // Every other action (pick_apple, speak, travel, trade, steal,
        // ...) had NO pause here at all — the moment an action
        // resolved, TakeTurn() ran again immediately, so on a fast model
        // three NPCs could chain thoughts/speech/results back to back
        // with nothing to actually read. MinTurnPause is a real floor
        // under every action. sleep no longer needs a case here at all —
        // by the time this line runs, NPCActor.ProcessSleeping() has
        // already held it in State.Sleeping for a real 20 seconds, so
        // adding yet another pause on top would just be stacking delays.
        // "attacked"/"attacked_while_sleeping" get NO pause at all —
        // these mean NPCActor.ReceiveDamage() just interrupted
        // whatever was happening because something is actively
        // attacking this NPC right now, and the very next TakeTurn()
        // is what routes into the fight/flee/freeze reflex (see
        // ActiveThreats()/HandleThreatTurn()). Waiting out the normal
        // pacing floor first would turn "instant reflex" into
        // "reflex, but only after standing there taking a free hit for
        // several seconds first."
        double pause = (reason == "attacked" || reason == "attacked_while_sleeping") ? 0.0
            : actionId == "wait" ? 1.0
            : MinTurnPause;
        await Actor.ToSignal(Actor.GetTree().CreateTimer(pause, processAlways: false), SceneTreeTimer.SignalName.Timeout);

        await TakeTurn();
    }
}
