using Godot;
using System.Collections.Generic;

// The player's avatar — input-controlled instead of LLM-controlled, but
// architecturally just another character: this class extends NPCActor
// directly rather than reimplementing movement, collision, inventory,
// stats, and action-resolution from scratch. Clicking a button in the
// action panel below calls AssignAction() — the exact same call
// NpcAgent makes after Mind.Decide() — so pick_apple, catch_fish,
// deposit, travel, follow, trade, and steal all execute
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
    private Button _gatherPineconeButton;
    private Button _gatherBerryButton;
    private Button _depositButton;
    private Button _travelButton;
    private Button _followButton;
    private Button _tradeButton;
    private Button _stealButton;
    private Button _sleepButton;
    private Button[] _allButtons; // fixed panel order — also what number-key 1-9 indexes into, see _quickActions below
    private Label _activeActionLabel;
    private ProgressBar _healthBar;
    private ProgressBar _fatigueBar;
    private Label _inventoryLabel;

    // Rebuilt every RefreshActionPanel() call — only the buttons that
    // are actually visible right now, in panel order. This is BOTH what
    // "press 1" through "press 9" index into and, implicitly, what "the
    // topmost available action" means, so a key and the button it
    // matches can never disagree about which is which.
    private readonly List<(Button Button, GameAction Action)> _quickActions = new();

    private GameAction _pickAppleTarget;
    private GameAction _catchFishTarget;
    private GameAction _gatherPineconeTarget;
    private GameAction _gatherBerryTarget;
    private GameAction _depositTarget;
    private GameAction _travelTarget;
    private GameAction _followTarget;
    private GameAction _tradeTarget;
    private GameAction _stealTarget;
    private static readonly GameAction SleepAction = new("sleep", "", 0f);

    // Same InventoryWatcher NpcAgent uses — no Memory to record into
    // here, so NoticeInventoryChanges() below just logs each note
    // instead, but the underlying comparison is the identical shared
    // implementation, not a second copy of it.
    private readonly InventoryWatcher _inventoryWatcher = new();

    public void Initialize(
        string displayName,
        WorldContext world,
        NpcThoughtLogger thoughtLog,
        System.Action<string, string> uiLog,
        LineEdit chatInput,
        Node actionPanel,
        Node vitalsPanel,
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

        _healthBar = vitalsPanel.GetNode<ProgressBar>("HealthBar");
        _fatigueBar = vitalsPanel.GetNode<ProgressBar>("FatigueBar");
        _inventoryLabel = vitalsPanel.GetNode<Label>("InventoryLabel");

        _activeActionLabel = actionPanel.GetNode<Label>("ActiveActionLabel");
        _pickAppleButton = actionPanel.GetNode<Button>("PickAppleButton");
        _catchFishButton = actionPanel.GetNode<Button>("CatchFishButton");
        _gatherPineconeButton = actionPanel.GetNode<Button>("GatherPineconeButton");
        _gatherBerryButton = actionPanel.GetNode<Button>("GatherBerryButton");
        _depositButton = actionPanel.GetNode<Button>("DepositButton");
        _travelButton = actionPanel.GetNode<Button>("TravelButton");
        _followButton = actionPanel.GetNode<Button>("FollowButton");
        _tradeButton = actionPanel.GetNode<Button>("TradeButton");
        _stealButton = actionPanel.GetNode<Button>("StealButton");
        _sleepButton = actionPanel.GetNode<Button>("SleepButton");
        _allButtons = new[] { _pickAppleButton, _catchFishButton, _gatherPineconeButton, _gatherBerryButton, _depositButton, _travelButton, _followButton, _tradeButton, _stealButton, _sleepButton };

        _pickAppleButton.Pressed += () => TryAssign(_pickAppleButton, _pickAppleTarget);
        _catchFishButton.Pressed += () => TryAssign(_catchFishButton, _catchFishTarget);
        _gatherPineconeButton.Pressed += () => TryAssign(_gatherPineconeButton, _gatherPineconeTarget);
        _gatherBerryButton.Pressed += () => TryAssign(_gatherBerryButton, _gatherBerryTarget);
        _depositButton.Pressed += () => TryAssign(_depositButton, _depositTarget);
        _travelButton.Pressed += () => TryAssign(_travelButton, _travelTarget);
        _followButton.Pressed += () => TryAssign(_followButton, _followTarget);
        _tradeButton.Pressed += () => TryAssign(_tradeButton, _tradeTarget);
        _stealButton.Pressed += () => TryAssign(_stealButton, _stealTarget);
        _sleepButton.Pressed += () => TryAssign(_sleepButton, SleepAction);

        ActionCompleted += OnMyActionCompleted;
        _inventoryWatcher.AbsorbOwnChange(Inventory);

        // Variant 5 (dark hair, red headband) — fixed and distinct from
        // the NPCs' 0/1/2 (see Main.CreatePlayer/NpcFactory) so the
        // player never happens to look identical to whichever NPC is
        // nearby. This also fixes a real gap: PlayerCharacter never had
        // any visual at all before this — NpcFactory gave every NPC a
        // ColorRect (now a sprite, same as this), but nothing ever gave
        // the player one, so it was invisible on screen.
        SetCharacterSprite(5);
        BuildVisibilityLight();
    }

    // Deliberately _UnhandledKeyInput, not an Input Map action — this
    // project polls WASD directly rather than touching project.godot's
    // [input] section (see the class comment history), same reasoning
    // here. This also gets "don't trigger while typing" for free: when
    // NameInput/ChatInput has focus, Godot's UI input system hands
    // number keys to it FIRST as normal typed characters and never lets
    // them reach unhandled input at all, so there's no separate
    // HasFocus() check needed here the way the WASD polling in
    // _PhysicsProcess needs one. Echo:false means a held-down key only
    // fires this once, not once per OS key-repeat tick.
    //
    // Space used to activate the topmost action here — moved to Main
    // (pause) instead, since number keys now cover "pick an action
    // without reaching for the mouse" more precisely (which one, not
    // just "whichever's on top").
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } keyEvent)
        {
            int quickIndex = QuickActionIndex(keyEvent.Keycode);
            if (quickIndex > 0)
            {
                SelectQuickAction(quickIndex);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    // 1-9, not an open-ended range — nine is already more actions than
    // the panel currently ever offers at once (eight, all gated by
    // proximity), and keeping it to the digit row is simpler than
    // reaching for a two-digit scheme that'll never actually get used.
    private static int QuickActionIndex(Key key) => key switch
    {
        Key.Key1 => 1, Key.Key2 => 2, Key.Key3 => 3, Key.Key4 => 4, Key.Key5 => 5,
        Key.Key6 => 6, Key.Key7 => 7, Key.Key8 => 8, Key.Key9 => 9,
        _ => 0,
    };

    private void SelectQuickAction(int oneBasedIndex)
    {
        int i = oneBasedIndex - 1;
        if (i < 0 || i >= _quickActions.Count)
            return; // no action currently sits at that slot — a silent no-op, not an error
        (Button button, GameAction action) = _quickActions[i];
        TryAssign(button, action);
    }

    // A radial PointLight2D sized to match what this player can actually
    // see/hear (SpatialMemory.VisionRadius, SpeechLog.HearingRadius —
    // both currently 260, kept as a live reference to those rather than
    // a separately-tuned number so this can never silently drift out of
    // sync with the real perception ranges). Works together with Main's
    // CanvasModulate — that dims the whole world to a flat baseline;
    // this ADDS brightness back on top within its radius, additively,
    // which is why the two need to exist together to get "dim except
    // near me," not either alone. Deliberately only on PlayerCharacter,
    // not NPCActor — this is specifically about showing a HUMAN player
    // what they can perceive; giving every NPC one too would light up
    // most of the village most of the time between however many of them
    // are on screen, defeating the point.
    private void BuildVisibilityLight()
    {
        float radius = Mathf.Max(SpatialMemory.VisionRadius, SpeechLog.HearingRadius);
        var light = new PointLight2D
        {
            Name = "VisibilityLight",
            Texture = VisibilityLightTexture(),
            TextureScale = (radius * 2f) / LightTextureSize,
            // The texture is fully opaque at its own center, so Energy
            // is the ONLY thing standing between "brightened back to
            // normal" and "blown out" right where the player stands —
            // 1.3 was adding a full extra 130% on top of Main's already-
            // dimmed base at the brightest point, overshooting well past
            // normal brightness. Lower, not higher, brings the center
            // down toward "normal" instead of "glaring."
            Energy = 0.6f,
        };
        AddChild(light);
    }

    // Built in code, not loaded from a file — a plain white-to-
    // transparent radial gradient is all a soft circular light needs,
    // and generating it avoids adding an asset file for something this
    // simple. Shared/cached the same way CharacterSpriteBuilder's sheet
    // is, so a second PlayerCharacter later reuses the same texture
    // rather than building its own copy.
    private const int LightTextureSize = 256;
    private static Texture2D _visibilityLightTexture;
    private static Texture2D VisibilityLightTexture()
    {
        if (_visibilityLightTexture != null)
            return _visibilityLightTexture;

        var gradient = new Gradient();
        gradient.SetColor(0, Colors.White);
        gradient.SetColor(1, new Color(1f, 1f, 1f, 0f));

        _visibilityLightTexture = new GradientTexture2D
        {
            Gradient = gradient,
            Width = LightTextureSize,
            Height = LightTextureSize,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
        };
        return _visibilityLightTexture;
    }

    private void TryAssign(Button sourceButton, GameAction action)
    {
        // Buttons only ever show for a target that was in range as of
        // the last scan, but the world can change between frames (a
        // tree could get depleted, another character could wander off)
        // — AssignAction() already handles that gracefully
        // (target_not_found), the same safety net an NPC gets.
        if (action == null || _state != State.Idle)
            return;
        AssignAction(action);
        EnterActiveMode(sourceButton.Text);
    }

    // Switches the panel from "choose one" to "doing it" the instant a
    // choice is made — every OTHER button hides immediately (there's
    // real time to fill now: NPCActor's own AttemptDuration, plus
    // whatever walking it takes to get there first) rather than staying
    // up and inviting a second click mid-action, and ActiveActionLabel
    // — the first child in the panel, so it's always the one thing "up
    // top" — takes over showing what's actually happening.
    // RefreshActionPanel() undoes this the moment _state is back to
    // Idle (see _PhysicsProcess), restoring every button to whatever's
    // actually available then; nothing here needs its own "undo" path.
    private void EnterActiveMode(string label)
    {
        foreach (Button b in _allButtons)
            b.Visible = false;

        // Strip a leading "N. " quick-key prefix (see RefreshActionPanel's
        // numbering pass) — the active label repeats what the action
        // actually is, not which key happened to trigger it.
        int prefix = label.IndexOf(". ");
        string clean = prefix is > 0 and <= 2 ? label[(prefix + 2)..] : label;

        _activeActionLabel.Text = $"▶ {clean}...";
        _activeActionLabel.Visible = true;
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
            return; // panel stays exactly as EnterActiveMode() left it until back to Idle — see that method's comment

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

        // Back to "choose one" — undoes whatever EnterActiveMode() did
        // for the action that just finished. Unconditional, not just
        // for whichever button was hidden, since which one that was
        // varies call to call.
        _activeActionLabel.Visible = false;

        _pickAppleTarget = NearestInRange(_world.Trees, ActionRanges.PickApple, i => $"tree_{i}", "pick_apple", ActionRanges.PickApple);
        _pickAppleButton.Visible = _pickAppleTarget != null;

        _catchFishTarget = NearestInRange(_world.FishingSpots, ActionRanges.CatchFish, i => $"fish_{i}", "catch_fish", ActionRanges.CatchFish);
        _catchFishButton.Visible = _catchFishTarget != null;

        _gatherPineconeTarget = NearestInRange(_world.PineTrees, ActionRanges.GatherPinecone, i => $"pine_{i}", "gather_pinecone", ActionRanges.GatherPinecone);
        _gatherPineconeButton.Visible = _gatherPineconeTarget != null;

        _gatherBerryTarget = NearestInRange(_world.BerryBushes, ActionRanges.GatherBerry, i => $"berry_{i}", "gather_berry", ActionRanges.GatherBerry);
        _gatherBerryButton.Visible = _gatherBerryTarget != null;

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

        // Same CanSleep() rule NPCs' tool schema is gated on — never
        // above SleepUnnecessaryThreshold, always below
        // LowFatigueThreshold, otherwise only near home. Was
        // unconditionally visible before; text still reflects urgency
        // for the cases where it does show.
        _sleepButton.Visible = CanSleep(_world.Home.GlobalPosition);
        _sleepButton.Text = Vitals.NeedsSleep ? "Sleep (exhausted)" : "Sleep";

        // Same panel order every time — this is what makes "press 1"
        // through "press 9" match "the buttons, top to bottom, skipping
        // whichever aren't showing right now" rather than needing its
        // own separately-maintained order.
        _quickActions.Clear();
        (Button Button, GameAction Action)[] candidates =
        {
            (_pickAppleButton, _pickAppleTarget),
            (_catchFishButton, _catchFishTarget),
            (_gatherPineconeButton, _gatherPineconeTarget),
            (_gatherBerryButton, _gatherBerryTarget),
            (_depositButton, _depositTarget),
            (_travelButton, _travelTarget),
            (_followButton, _followTarget),
            (_tradeButton, _tradeTarget),
            (_stealButton, _stealTarget),
            (_sleepButton, _sleepButton.Visible ? SleepAction : null),
        };
        foreach ((Button button, GameAction action) in candidates)
            if (action != null)
                _quickActions.Add((button, action));

        // "1. Pick Apple", "2. Catch Fish", ... — so the shortcut is
        // visible on the button itself, not something you have to
        // already know. Strips any number this same loop set on a
        // PREVIOUS call before reapplying, since which slot a button
        // lands in shifts as targets come in and out of range.
        for (int i = 0; i < _quickActions.Count; i++)
        {
            Button button = _quickActions[i].Button;
            string baseText = button.Text;
            int existingPrefix = baseText.IndexOf(". ");
            string clean = existingPrefix is > 0 and <= 2 ? baseText[(existingPrefix + 2)..] : baseText;
            button.Text = $"{i + 1}. {clean}";
        }

        RefreshVitalsPanel();
        NoticeInventoryChanges();
    }

    // An NPC gets its own condition read out in plain text every turn
    // (BuildPerception's "Your physical condition: ..." line) — the
    // player never had ANY equivalent, so there was no way to actually
    // see Fatigue creeping toward NeedsSleep short of guessing. Same
    // Vitals values NpcAgent's line describes, just as bars instead of
    // a sentence, since the player reads screen state instead of a
    // written perception block.
    private void RefreshVitalsPanel()
    {
        _healthBar.Value = Vitals.Health;
        _fatigueBar.Value = Vitals.Fatigue;
        // Same red flag NeedsSleep already drives on the Sleep button's
        // text — here as a color instead of a word, so it's visible at
        // a glance without reading the bar's number.
        _fatigueBar.Modulate = Vitals.NeedsSleep ? new Color(0.9f, 0.35f, 0.3f) : Colors.White;
        _inventoryLabel.Text = DescribeInventory();
    }

    // A small, fixed emoji per item type reads faster at a glance than
    // the plain-text "3 apples, 1 fish" Inventory.Describe() gives NPCs
    // (that one's meant for an LLM prompt, not a corner of the screen) —
    // falls back to the bare item name for anything not in the map yet,
    // so a future item type never renders as a blank icon.
    private static readonly Dictionary<string, string> ItemEmoji = new()
    {
        { "apple", "🍎" }, { "fish", "🐟" }, { "pinecone", "🌲" },
        { "blueberry", "🫐" }, { "blackberry", "🍇" }, { "raspberry", "🍓" },
    };

    private string DescribeInventory()
    {
        if (Inventory.All.Count == 0)
            return "carrying nothing";
        var parts = new List<string>();
        foreach (KeyValuePair<string, int> kv in Inventory.All)
        {
            string icon = ItemEmoji.TryGetValue(kv.Key, out string e) ? e : kv.Key;
            parts.Add($"{icon} x{kv.Value}");
        }
        return string.Join("   ", parts);
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

    // Feeds WorldEventLog so nearby NPCs can notice the PLAYER'S own
    // non-secret actions on their own next turn — same shape as
    // NpcAgent's own AnnounceVisibleAction(), just no Memory/thought-log
    // side to also update. Only called for actions other than
    // trade/steal (see OnMyActionCompleted below) — trade announces
    // itself right where its "other"/item detail already lives, and
    // steal deliberately never goes through here (see
    // AnnounceStealthAttempt instead).
    private void AnnounceVisibleAction(string actionId, string targetId, Godot.Collections.Dictionary data)
    {
        string description = actionId switch
        {
            "pick_apple" => $"{DisplayName} picks an apple from a tree.",
            "catch_fish" => $"{DisplayName} catches a fish.",
            "gather_pinecone" => $"{DisplayName} gathers a pinecone from a pine tree.",
            "gather_berry" => $"{DisplayName} picks a berry from a bush.",
            "deposit" => $"{DisplayName} deposits their haul at home.",
            "travel" => $"{DisplayName} arrives at {targetId}.",
            "follow" => $"{DisplayName} walks up alongside {targetId}.",
            "sleep" => $"{DisplayName} was asleep nearby for a while.",
            _ => null,
        };
        if (description != null)
            WorldEventLog.Announce(DisplayName, GlobalPosition, description);
    }

    private void OnMyActionCompleted(Godot.Collections.Dictionary result)
    {
        bool success = result["success"].AsBool();
        string actionId = result["action"].AsString();
        string reason = result["reason"].AsString();
        var data = result["data"].AsGodotDictionary();
        string rollSummary = SkillCheck.SummarizeData(data);
        string color = success ? "8fd694" : "e0876b";
        _uiLog?.Invoke($"[{DisplayName}] -> {actionId}: {(success ? "OK" : "FAILED")} ({reason}){rollSummary}", color);
        _thoughtLog?.Log(DisplayName, "ACTION_RESULT", $"{actionId} {(success ? "OK" : "FAILED")} ({reason}){rollSummary}");

        if (success && (actionId == "trade" || actionId == "steal"))
        {
            string item = data["item"].AsString();
            int amount = data["amount"].AsInt32();
            string other = actionId == "trade" ? data["given_to"].AsString() : data["stolen_from"].AsString();
            string verb = actionId == "trade" ? "gives" : "takes";
            string prep = actionId == "trade" ? "to" : "from";
            _uiLog?.Invoke($"[{DisplayName}] {verb} {amount} {item}(s) {prep} {other}{rollSummary}", "e8d9a9");

            if (actionId == "trade")
            {
                // A real, public exchange — visible to every nearby NPC
                // on their own next turn, not just the recipient.
                WorldEventLog.Announce(DisplayName, GlobalPosition, $"{DisplayName} gives {amount} {item}(s) to {other}.");
            }
            else
            {
                // Deliberately never goes through the normal broadcast
                // — see WorldEventLog.AnnounceStealthAttempt's own
                // comment. Only whoever's own Wisdom roll beats this
                // Dexterity ever has a chance to find out.
                WorldEventLog.AnnounceStealthAttempt(
                    DisplayName, other, GlobalPosition,
                    $"You notice {DisplayName} take something from {other} without asking.",
                    Stats.DexterityMod, _world.CharactersWithStats());
            }
        }
        else if (success)
        {
            AnnounceVisibleAction(actionId, result["target_id"].AsString(), data);
        }

        // Absorb my own action's effect on inventory into the baseline
        // now, before the next RefreshActionPanel() comparison — same
        // ordering NpcAgent relies on, see InventoryWatcher.
        // AbsorbOwnChange()'s comment for why it matters.
        _inventoryWatcher.AbsorbOwnChange(Inventory);
    }

    // See InventoryWatcher for the actual comparison. Unlike NpcAgent
    // (which skips the console line for a gain, since Memory already
    // carries it), the player has no Memory to fall back on, so both
    // loss and gain get a console line here — otherwise a gain would
    // never surface anywhere at all.
    private void NoticeInventoryChanges()
    {
        foreach (string note in _inventoryWatcher.DetectExternalChanges(Inventory))
        {
            bool isLoss = note.Contains("missing");
            _uiLog?.Invoke($"[{DisplayName}] {note}", isLoss ? "e0876b" : "a9c9e8");
            _thoughtLog?.Log(DisplayName, isLoss ? "INVENTORY_LOSS_NOTICED" : "INVENTORY_GAIN_NOTICED", note);
        }
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
        ShowSpeechBubble();
        _uiLog?.Invoke($"[{DisplayName}] says: \"{text}\"", "e8d9a9");
        _thoughtLog?.Log(DisplayName, "SPEAK", text);
    }
}
