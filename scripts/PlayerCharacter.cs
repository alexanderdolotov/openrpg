using Godot;
using System.Collections.Generic;

// The player's avatar — input-controlled instead of LLM-controlled, but
// architecturally just another character: this class extends NPCActor
// directly rather than reimplementing movement, collision, inventory,
// stats, and action-resolution from scratch. Clicking a button in the
// action panel below calls AssignAction() — the exact same call
// NpcAgent makes after Mind.Decide() — so pick_apple, catch_fish,
// deposit, travel, follow, trade, steal, and persuade all execute
// through the identical AssignAction()/ProcessAttempting()/
// TryInteract() (or SkillCheck) pipeline an NPC's decision does. The
// only real difference from an NPC is WHAT decides the action: WASD and
// UI clicks here, Mind.Decide() there.
public partial class PlayerCharacter : NPCActor, IWorldCharacter
{
    // Two keys per direction so a single set of WASD-and-arrows works
    // for one player without any config — but this is a per-instance
    // field, not a constant, specifically so a second PlayerCharacter
    // (a second human player, sharing the same keyboard or not) can be
    // constructed with an entirely different KeyBindings and nothing
    // else about this class needs to change.
    public readonly struct KeyBindings
    {
        public readonly Key Up, Down, Left, Right, UpAlt, DownAlt, LeftAlt, RightAlt;
        public KeyBindings(Key up, Key down, Key left, Key right, Key upAlt = Key.None, Key downAlt = Key.None, Key leftAlt = Key.None, Key rightAlt = Key.None)
        {
            Up = up; Down = down; Left = left; Right = right;
            UpAlt = upAlt; DownAlt = downAlt; LeftAlt = leftAlt; RightAlt = rightAlt;
        }

        public static readonly KeyBindings WasdAndArrows = new(Key.W, Key.S, Key.A, Key.D, Key.Up, Key.Down, Key.Left, Key.Right);
    }

    private const float FreeMoveSpeed = 160f; // a bit faster than NPCActor's 120 — direct control reads better a little quicker

    public string Id { get; private set; } = "player";
    public string DisplayName { get; private set; } = "Player";

    private WorldContext _world;
    private LineEdit _chatInput;
    private NpcThoughtLogger _thoughtLog;
    private System.Action<string, string> _uiLog;
    private KeyBindings _keys;

    private Button _pickAppleButton;
    private Button _catchFishButton;
    private Button _depositButton;
    private Button _travelButton;
    private Button _followButton;
    private Button _tradeButton;
    private Button _stealButton;
    private Button _persuadeButton;
    private Button _sleepButton;

    private GameAction _pickAppleTarget;
    private GameAction _catchFishTarget;
    private GameAction _depositTarget;
    private GameAction _travelTarget;
    private GameAction _followTarget;
    private GameAction _tradeTarget;
    private GameAction _stealTarget;
    private IWorldCharacter _persuadeTarget; // no canned message to bake into a GameAction ahead of time — see TryPersuade()
    private static readonly GameAction SleepAction = new("sleep", "", 0f);

    // Baseline for noticing an inventory change caused by someone else
    // — same mechanism and same reasoning as NpcAgent's field of the
    // same name (see its comment); duplicated rather than shared
    // because NpcAgent's version is wired into its Memory/turn loop,
    // which the player doesn't have.
    private Dictionary<string, int> _lastKnownInventory = new();

    public void Initialize(
        string displayName,
        WorldContext world,
        NpcThoughtLogger thoughtLog,
        System.Action<string, string> uiLog,
        LineEdit chatInput,
        Node actionPanel,
        string playerId = "player",
        KeyBindings? keys = null)
    {
        Id = playerId;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName;
        _world = world;
        _thoughtLog = thoughtLog;
        _uiLog = uiLog;
        _chatInput = chatInput;
        _keys = keys ?? KeyBindings.WasdAndArrows;
        _chatInput.TextSubmitted += OnChatSubmitted;

        _pickAppleButton = actionPanel.GetNode<Button>("PickAppleButton");
        _catchFishButton = actionPanel.GetNode<Button>("CatchFishButton");
        _depositButton = actionPanel.GetNode<Button>("DepositButton");
        _travelButton = actionPanel.GetNode<Button>("TravelButton");
        _followButton = actionPanel.GetNode<Button>("FollowButton");
        _tradeButton = actionPanel.GetNode<Button>("TradeButton");
        _stealButton = actionPanel.GetNode<Button>("StealButton");
        _persuadeButton = actionPanel.GetNode<Button>("PersuadeButton");
        _sleepButton = actionPanel.GetNode<Button>("SleepButton");

        _pickAppleButton.Pressed += () => TryAssign(_pickAppleTarget);
        _catchFishButton.Pressed += () => TryAssign(_catchFishTarget);
        _depositButton.Pressed += () => TryAssign(_depositTarget);
        _travelButton.Pressed += () => TryAssign(_travelTarget);
        _followButton.Pressed += () => TryAssign(_followTarget);
        _tradeButton.Pressed += () => TryAssign(_tradeTarget);
        _stealButton.Pressed += () => TryAssign(_stealTarget);
        _persuadeButton.Pressed += TryPersuade;
        _sleepButton.Pressed += () => TryAssign(SleepAction);

        ActionCompleted += OnMyActionCompleted;
        _lastKnownInventory = Inventory.Snapshot();

        // Variant 5 (dark hair, red headband) — fixed and distinct from
        // the NPCs' 0/1/2 (see Main.CreatePlayer/NpcFactory) so the
        // player never happens to look identical to whichever NPC is
        // nearby. This also fixes a real gap: PlayerCharacter never had
        // any visual at all before this — NpcFactory gave every NPC a
        // ColorRect (now a sprite, same as this), but nothing ever gave
        // the player one, so it was invisible on screen.
        SetCharacterSprite(5);
    }

    private void TryAssign(GameAction action)
    {
        // Buttons only ever show for a target that was in range as of
        // the last scan, but the world can change between frames (a
        // tree could get depleted, another character could wander off)
        // — AssignAction() already handles that gracefully
        // (target_not_found), the same safety net an NPC gets.
        if (action == null || _state != State.Idle)
            return;
        AssignAction(action);
    }

    // Persuade needs a real pitch, not a canned string a button click
    // alone can supply — so it borrows the chat box as the message
    // source, same box "speak" uses, instead of a separate text field.
    private void TryPersuade()
    {
        if (_persuadeTarget == null || _state != State.Idle)
            return;
        string pitch = _chatInput.Text.Trim();
        if (pitch == "")
        {
            _uiLog?.Invoke($"[{DisplayName}] (type what you're trying to convince {_persuadeTarget.DisplayName} of in the chat box, then click Persuade)", "e0c66a");
            return;
        }
        _chatInput.Text = "";
        AssignAction(new GameAction("persuade", _persuadeTarget.DisplayName, ActionRanges.Persuade, message: pitch));
    }

    public override void _PhysicsProcess(double delta)
    {
        // Always runs first — this is what decays Vitals.Fatigue the
        // same way for the player as for any NPC. It also drives
        // navigation/attempting when an action is in progress, and is a
        // harmless no-op the rest of the time (NPCActor's switch has no
        // Idle case), so free movement below stays safe to run right
        // after it whenever the player IS idle.
        base._PhysicsProcess(delta);

        if (_state != State.Idle)
        {
            RefreshActionPanel();
            return;
        }

        // Idle: free movement under direct player control. This is the
        // one genuine behavioral difference from an NPC — what DECIDES
        // to move, not how movement or collision itself works.
        if (_chatInput != null && _chatInput.HasFocus())
        {
            Velocity = Vector2.Zero;
        }
        else
        {
            Vector2 direction = Vector2.Zero;
            if (Input.IsKeyPressed(_keys.Left) || Input.IsKeyPressed(_keys.LeftAlt)) direction.X -= 1f;
            if (Input.IsKeyPressed(_keys.Right) || Input.IsKeyPressed(_keys.RightAlt)) direction.X += 1f;
            if (Input.IsKeyPressed(_keys.Up) || Input.IsKeyPressed(_keys.UpAlt)) direction.Y -= 1f;
            if (Input.IsKeyPressed(_keys.Down) || Input.IsKeyPressed(_keys.DownAlt)) direction.Y += 1f;
            Velocity = direction.Normalized() * FreeMoveSpeed;
            MoveAndSlide();
        }

        // base._PhysicsProcess() above already called this once, but with
        // last frame's Velocity — this class's own Idle branch is what
        // actually sets this frame's Velocity, and it runs after that
        // call, so the sprite needs a second, corrected pass here. Everywhere
        // else (Navigating/Attempting), the one call inside NPCActor's
        // own _PhysicsProcess is already using fresh Velocity and this
        // branch never even runs.
        UpdateSpriteFacing();

        RefreshActionPanel();
    }

    // Same target lists, same ActionRanges, same item/stat rules an LLM
    // decision would be constrained to — deliberately not a separate
    // rule set, just answered by proximity and a button instead of a
    // tool-call. Keeps to one candidate per action slot (the nearest
    // valid target) rather than dynamically growing/shrinking a button
    // list, so nothing here creates or destroys UI nodes every frame.
    private void RefreshActionPanel()
    {
        if (_world == null) return;

        _pickAppleTarget = NearestInRange(_world.Trees, ActionRanges.PickApple, i => $"tree_{i}", "pick_apple", ActionRanges.PickApple);
        _pickAppleButton.Visible = _pickAppleTarget != null;

        _catchFishTarget = NearestInRange(_world.FishingSpots, ActionRanges.CatchFish, i => $"fish_{i}", "catch_fish", ActionRanges.CatchFish);
        _catchFishButton.Visible = _catchFishTarget != null;

        float homeDist = GlobalPosition.DistanceTo(_world.Home.GlobalPosition);
        _depositTarget = homeDist <= ActionRanges.Deposit ? new GameAction("deposit", "home", ActionRanges.Deposit) : null;
        _depositButton.Visible = _depositTarget != null;

        _travelTarget = null;
        foreach (KeyValuePair<string, Node2D> kv in _world.Flagpoles)
        {
            if (GlobalPosition.DistanceTo(kv.Value.GlobalPosition) <= ActionRanges.Travel)
            {
                _travelTarget = new GameAction("travel", kv.Key, ActionRanges.Travel);
                _travelButton.Text = $"Travel to {kv.Key}";
                break;
            }
        }
        _travelButton.Visible = _travelTarget != null;

        IWorldCharacter nearest = NearestOtherCharacter(ActionRanges.Follow);
        _followTarget = nearest != null ? new GameAction("follow", nearest.DisplayName, ActionRanges.Follow) : null;
        if (nearest != null) _followButton.Text = $"Follow {nearest.DisplayName}";
        _followButton.Visible = _followTarget != null;

        IWorldCharacter tradeTarget = NearestOtherCharacter(ActionRanges.Trade);
        string firstCarried = null;
        foreach (string item in Inventory.All.Keys) { firstCarried = item; break; }
        _tradeTarget = (tradeTarget != null && firstCarried != null)
            ? new GameAction("trade", tradeTarget.DisplayName, ActionRanges.Trade, item: firstCarried, amount: 1)
            : null;
        if (_tradeTarget != null) _tradeButton.Text = $"Give 1 {firstCarried} → {tradeTarget.DisplayName}";
        _tradeButton.Visible = _tradeTarget != null;

        // You don't know what's in someone else's pockets any more than
        // an NPC does — the button always offers "apple" as the guess,
        // same blind gamble, not a hint about what they're carrying.
        IWorldCharacter stealTarget = NearestOtherCharacter(ActionRanges.Steal);
        _stealTarget = stealTarget != null ? new GameAction("steal", stealTarget.DisplayName, ActionRanges.Steal, item: "apple", amount: 1) : null;
        if (_stealTarget != null) _stealButton.Text = $"Steal from {stealTarget.DisplayName}";
        _stealButton.Visible = _stealTarget != null;

        _persuadeTarget = NearestOtherCharacter(ActionRanges.Persuade);
        if (_persuadeTarget != null) _persuadeButton.Text = $"Persuade {_persuadeTarget.DisplayName}";
        _persuadeButton.Visible = _persuadeTarget != null;

        // Always available (like NPCs' sleep tool — never gated on
        // proximity), text just reflects how urgent it actually is.
        _sleepButton.Text = Vitals.NeedsSleep ? "Sleep (exhausted)" : "Sleep";

        NoticeInventoryChanges();
    }

    private IWorldCharacter NearestOtherCharacter(float range)
    {
        IWorldCharacter best = null;
        float bestDist = float.MaxValue;
        foreach (IWorldCharacter other in _world.Agents)
        {
            if (other.Id == Id) continue;
            float d = GlobalPosition.DistanceTo(other.GlobalPosition);
            if (d <= range && d < bestDist) { bestDist = d; best = other; }
        }
        return best;
    }

    private GameAction NearestInRange<T>(List<T> items, float range, System.Func<int, string> idFor, string actionId, float actionRange) where T : Node2D
    {
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < items.Count; i++)
        {
            float d = GlobalPosition.DistanceTo(items[i].GlobalPosition);
            if (d <= range && d < bestDist) { bestDist = d; best = i; }
        }
        return best < 0 ? null : new GameAction(actionId, idFor(best), actionRange);
    }

    private void OnMyActionCompleted(Godot.Collections.Dictionary result)
    {
        bool success = result["success"].AsBool();
        string actionId = result["action"].AsString();
        string targetId = result["target_id"].AsString();
        string message = result["message"].AsString();
        string reason = result["reason"].AsString();
        var data = result["data"].AsGodotDictionary();
        string rollSummary = SkillCheck.SummarizeData(data);
        string color = success ? "8fd694" : "e0876b";
        _uiLog?.Invoke($"[{DisplayName}] -> {actionId}: {(success ? "OK" : "FAILED")} ({reason}){rollSummary}", color);
        _thoughtLog?.Log(DisplayName, "ACTION_RESULT", $"{actionId} {(success ? "OK" : "FAILED")} ({reason}){rollSummary}");

        if (actionId == "persuade")
        {
            // Same delivery NpcAgent.OnActionCompleted() uses — through
            // SpeechLog, gated by hearing radius, framed by the roll's
            // outcome, never forcing what the target decides next.
            string framing = success ? "tries hard to convince you" : "tries to convince you, but doesn't seem very persuasive";
            SpeechLog.Say(DisplayName, GlobalPosition, $"({framing}) {message}");
            _uiLog?.Invoke($"[{DisplayName}] {(success ? "persuasively" : "unconvincingly")} tells {targetId}: \"{message}\"{rollSummary}", success ? "e8d9a9" : "e0876b");
            _thoughtLog?.Log(DisplayName, "PERSUADE", $"{targetId}: \"{message}\" ({(success ? "persuasive" : "unconvincing")}){rollSummary}");
        }

        if (success && (actionId == "trade" || actionId == "steal"))
        {
            string item = data["item"].AsString();
            int amount = data["amount"].AsInt32();
            string other = actionId == "trade" ? data["given_to"].AsString() : data["stolen_from"].AsString();
            string verb = actionId == "trade" ? "gives" : "takes";
            string prep = actionId == "trade" ? "to" : "from";
            _uiLog?.Invoke($"[{DisplayName}] {verb} {amount} {item}(s) {prep} {other}{rollSummary}", "e8d9a9");
        }

        // Absorb my own action's effect on inventory into the baseline
        // now, before the next RefreshActionPanel() comparison — same
        // ordering NpcAgent relies on, see its comment for why it matters.
        _lastKnownInventory = Inventory.Snapshot();
    }

    // Same mechanism as NpcAgent.NoticeInventoryChanges() — see there for
    // the full reasoning. Called every frame from RefreshActionPanel()
    // rather than once per "turn" (the player doesn't have turns), but
    // the bracketing is identical: OnMyActionCompleted() always refreshes
    // the baseline synchronously, in the same physics frame, before this
    // runs — so a diff found here can only be someone else's doing.
    private void NoticeInventoryChanges()
    {
        foreach (KeyValuePair<string, int> before in _lastKnownInventory)
        {
            int after = Inventory.Count(before.Key);
            if (after < before.Value)
            {
                int missing = before.Value - after;
                string note = $"You notice you're missing {missing} {before.Key}{(missing != 1 ? "s" : "")} you had before — you didn't deposit, give, or trade it away, so it must have been taken or lost.";
                _uiLog?.Invoke($"[{DisplayName}] {note}", "e0876b");
                _thoughtLog?.Log(DisplayName, "INVENTORY_LOSS_NOTICED", note);
            }
        }
        foreach (KeyValuePair<string, int> after in Inventory.All)
        {
            int before = _lastKnownInventory.TryGetValue(after.Key, out int b) ? b : 0;
            if (after.Value > before)
            {
                int gained = after.Value - before;
                string note = $"You notice you now have {gained} more {after.Key}{(gained != 1 ? "s" : "")} than before — someone must have given or traded it to you.";
                _uiLog?.Invoke($"[{DisplayName}] {note}", "a9c9e8");
                _thoughtLog?.Log(DisplayName, "INVENTORY_GAIN_NOTICED", note);
            }
        }
        _lastKnownInventory = Inventory.Snapshot();
    }

    private void OnChatSubmitted(string text)
    {
        text = text.Trim();
        _chatInput.Text = "";
        _chatInput.ReleaseFocus();
        if (text == "")
            return;

        // Exactly the same call NpcAgent.OnActionCompleted() makes for
        // "speak" — the player is heard the same way, through the same
        // shared log, with the same "heard once, remembered" semantics
        // on the listening side.
        SpeechLog.Say(DisplayName, GlobalPosition, text);
        _uiLog?.Invoke($"[{DisplayName}] says: \"{text}\"", "e8d9a9");
        _thoughtLog?.Log(DisplayName, "SPEAK", text);
    }
}
