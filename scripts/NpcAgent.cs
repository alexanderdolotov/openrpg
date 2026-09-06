using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Everything one NPC needs to run its own cognition loop, owned
// together: its actor, its own Mind (with its own ILlmProvider instance
// — never shared between agents, since a single HttpRequest node can't
// have two requests in flight at once), its memory, and its
// personality. Built by NpcFactory, not constructed directly.
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

    // Baseline for noticing an inventory change caused by someone ELSE
    // (a trade received, a theft) rather than this NPC's own last
    // action. Refreshed right after this NPC's own action resolves (see
    // OnActionCompleted) so that refresh always absorbs the NPC's own
    // effect first — anything different by the next comparison, at the
    // top of the following TakeTurn(), can only be someone else's doing.
    private Dictionary<string, int> _lastKnownInventory = new();

    private static readonly Random Rng = new();
    private const float RetryDelaySeconds = 3f;

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

        AddChild(Actor);
        AddChild((Node)provider);
        Actor.ActionCompleted += OnActionCompleted;
    }

    public void Start() => _ = TakeTurn();

    private async Task TakeTurn()
    {
        if (_thinking) return;
        _thinking = true;
        _uiLog($"[{Personality.Name}] ...thinking...", "6f8068");

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
        // be live.
        foreach ((string speakerName, string message) in SpeechLog.Overheard(Personality.Name, Actor.GlobalPosition))
        {
            _uiLog($"[{Personality.Name}] heard {speakerName} say: \"{message}\"", "c9a9e8");
            Memory.Record("heard", $"{speakerName} said: \"{message}\"");
            _thoughtLog.Log(Personality.Name, "HEARD", $"{speakerName}: {message}");
        }

        string perception = BuildPerception();
        var result = await Mind.Decide(perception, TreeIds(), FishingSpotIds(), TravelTargetIds(), NearbyNpcNames(), CarriedItems(), Personality);
        _thinking = false;

        if (!result.Ok && _pureLlmMode)
        {
            // Pure LLM mode: no substitute action, ever. Stand still and
            // retry rather than let a fallback quietly stand in for a
            // decision — the whole point of this mode is that every
            // action seen genuinely came from the model.
            _uiLog($"[{Personality.Name}] mind unreachable ({result.Error}) -> retrying in {RetryDelaySeconds:0}s (fallback disabled)", "e0c66a");
            _thoughtLog.Log(Personality.Name, "FALLBACK_DISABLED", result.Error);
            await Actor.ToSignal(Actor.GetTree().CreateTimer(RetryDelaySeconds), SceneTreeTimer.SignalName.Timeout);
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
        else
        {
            _uiLog($"[{Personality.Name}] mind unreachable ({result.Error}) -> random fallback", "e0c66a");
            _thoughtLog.Log(Personality.Name, "FALLBACK", result.Error);
            action = RandomFallback();
        }

        string targetNote = action.TargetId != "" ? $" -> {action.TargetId}" : "";
        _uiLog($"[{Personality.Name}] attempting: {action.Id}{targetNote}", "d8ddd0");
        _thoughtLog.Log(Personality.Name, "ACTION_ATTEMPT", $"{action.Id}{targetNote}");
        Actor.AssignAction(action);
    }

    private string[] TreeIds()
    {
        var ids = new string[_world.Trees.Count];
        for (int i = 0; i < _world.Trees.Count; i++)
            ids[i] = $"tree_{i}";
        return ids;
    }

    private string[] FishingSpotIds()
    {
        var ids = new string[_world.FishingSpots.Count];
        for (int i = 0; i < _world.FishingSpots.Count; i++)
            ids[i] = $"fish_{i}";
        return ids;
    }

    private string[] TravelTargetIds()
    {
        var ids = new string[_world.Flagpoles.Count];
        _world.Flagpoles.Keys.CopyTo(ids, 0);
        return ids;
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
        yield return ("home", _world.Home.GlobalPosition);
        foreach (KeyValuePair<string, Node2D> kv in _world.Flagpoles)
            yield return (kv.Key, kv.Value.GlobalPosition);
    }

    private string BuildPerception()
    {
        var lines = new List<string> { Personality.DescribeForPrompt() };
        if (_lastResultLine != "")
            lines.Add(_lastResultLine);
        for (int i = 0; i < _world.Trees.Count; i++)
        {
            AppleTree t = _world.Trees[i];
            int dist = (int)Actor.GlobalPosition.DistanceTo(t.GlobalPosition);
            lines.Add($"tree_{i}: {t.AppleCount} apples left, {dist} px away");
        }
        for (int i = 0; i < _world.FishingSpots.Count; i++)
        {
            FishingSpot f = _world.FishingSpots[i];
            int dist = (int)Actor.GlobalPosition.DistanceTo(f.GlobalPosition);
            lines.Add($"fish_{i}: {f.FishCount} fish left, {dist} px away");
        }
        int homeDist = (int)Actor.GlobalPosition.DistanceTo(_world.Home.GlobalPosition);
        lines.Add($"home: {homeDist} px away, {_world.Home.ApplesStored} apples and {_world.Home.FishStored} fish stored there so far");

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
        foreach (KeyValuePair<string, Node2D> kv in _world.Flagpoles)
        {
            float d = Actor.GlobalPosition.DistanceTo(kv.Value.GlobalPosition);
            if (d < bestDist) { bestDist = d; nearest = kv.Key; }
        }
        return $"near {nearest} ({(int)bestDist}px away)";
    }

    // Used only when the mind is unreachable — not personality-aware on
    // purpose, since a network fallback isn't a real decision. Rather
    // than a fixed priority order, it picks randomly among whatever's
    // actually valid right now (never a nonsensical option, like
    // depositing nothing or picking an empty tree). Deliberately never
    // picks speak/travel/follow/trade/steal — a network outage isn't a
    // social or exploratory impulse, it's a fallback for the core
    // resource loop.
    private GameAction RandomFallback()
    {
        // A physical need, not a social or exploratory impulse — checked
        // even before deposit, same reasoning as everything else in this
        // fallback: basic self-maintenance still makes sense with no
        // mind reachable to actually decide anything.
        if (Actor.Vitals.NeedsSleep)
            return new GameAction("sleep", "", 0f);

        if (Actor.Inventory.All.Count > 0)
            return new GameAction("deposit", "home", ActionRanges.Deposit);

        var options = new List<GameAction>();
        for (int i = 0; i < _world.Trees.Count; i++)
            if (_world.Trees[i].AppleCount > 0)
                options.Add(new GameAction("pick_apple", $"tree_{i}", ActionRanges.PickApple));
        for (int i = 0; i < _world.FishingSpots.Count; i++)
            if (_world.FishingSpots[i].FishCount > 0)
                options.Add(new GameAction("catch_fish", $"fish_{i}", ActionRanges.CatchFish));

        if (options.Count == 0)
            return new GameAction("wait", "", 0f);

        return options[Rng.Next(options.Count)];
    }

    // Reasons that mean "bad luck," not "wrong choice" — a fumbled gather,
    // a failed steal, an unconvincing persuade attempt are all real dice
    // rolls that could go the other way next time on the exact same
    // target. Conflating these with a structural block (depleted,
    // hands full, target gone — where repeating really is pointless
    // until something changes) was actively steering NPCs away from a
    // perfectly good tree after two unlucky rolls in a row.
    private static readonly HashSet<string> ChanceBasedReasons = new() { "fumbled", "steal_failed", "unconvincing" };

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

    // Compares current inventory against the baseline taken right after
    // this NPC's own last action resolved — since nothing else can
    // legally change an inventory except this NPC's own actions
    // (already accounted for in that baseline) or someone else's
    // trade/steal reaching them, any difference found here can only be
    // the latter. This is the whole mechanism behind "wonder if an item
    // was lost or stolen": no flag anywhere says "you were robbed" —
    // the NPC just notices its own count doesn't match what it
    // remembers and has to decide what that means, same as a person
    // patting their pockets.
    private void NoticeInventoryChanges()
    {
        foreach (KeyValuePair<string, int> before in _lastKnownInventory)
        {
            int after = Actor.Inventory.Count(before.Key);
            if (after < before.Value)
            {
                int missing = before.Value - after;
                string note = $"You notice you're missing {missing} {before.Key}{(missing != 1 ? "s" : "")} you had before — you didn't deposit, give, or trade it away yourself, so it must have been taken or lost.";
                _uiLog($"[{Personality.Name}] {note}", "e0876b");
                Memory.Record("inventory", note);
                _thoughtLog.Log(Personality.Name, "INVENTORY_LOSS_NOTICED", note);
            }
        }
        foreach (KeyValuePair<string, int> after in Actor.Inventory.All)
        {
            int before = _lastKnownInventory.TryGetValue(after.Key, out int b) ? b : 0;
            if (after.Value > before)
            {
                int gained = after.Value - before;
                string note = $"You notice you now have {gained} more {after.Key}{(gained != 1 ? "s" : "")} than you remember having — someone must have given or traded it to you.";
                Memory.Record("inventory", note);
                _thoughtLog.Log(Personality.Name, "INVENTORY_GAIN_NOTICED", note);
            }
        }
        _lastKnownInventory = Actor.Inventory.Snapshot();
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
            }
        }

        if (actionId == "persuade")
        {
            // Delivered through SpeechLog exactly like speak — same
            // hearing-radius gate, same "heard once, remembered"
            // semantics — just with the roll's outcome folded into the
            // framing the target actually hears. The target's own Mind
            // still decides what to do about it next turn; this never
            // picks for them (see Mind's persuade tool description).
            string framing = success ? "tries hard to convince you" : "tries to convince you, but doesn't seem very persuasive";
            SpeechLog.Say(Personality.Name, Actor.GlobalPosition, $"({framing}) {message}");
            _uiLog($"[{Personality.Name}] {(success ? "persuasively" : "unconvincingly")} tells {targetId}: \"{message}\"{rollSummary}", success ? "e8d9a9" : "e0876b");
            Memory.Record("persuade", $"Tried to persuade {targetId}: \"{message}\" — {(success ? "landed persuasively" : "fell flat")}.");
            _thoughtLog.Log(Personality.Name, "PERSUADE", $"{targetId}: \"{message}\" ({(success ? "persuasive" : "unconvincing")}){rollSummary}");
        }

        UpdateLastResult(success, actionId, targetId, reason);

        // Absorb THIS action's own effect on inventory into the baseline
        // now, before TakeTurn() runs again below — see
        // NoticeInventoryChanges()'s comment for why the ordering matters.
        _lastKnownInventory = Actor.Inventory.Snapshot();

        if (Memory.NeedsCompression)
        {
            _uiLog($"[{Personality.Name}] ...compressing memory...", "6f8068");
            await Memory.CompressIfNeeded(Mind);
            _uiLog($"[{Personality.Name}] diary: {Memory.Diary}", "a9c9e8");
            _thoughtLog.Log(Personality.Name, "MEMORY_COMPRESSED", Memory.Diary);
        }

        if (actionId == "wait")
            await Actor.ToSignal(Actor.GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        else if (actionId == "sleep")
            await Actor.ToSignal(Actor.GetTree().CreateTimer(4.0), SceneTreeTimer.SignalName.Timeout);

        await TakeTurn();
    }
}
