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
            string hint = DescribePersuasionHint(speakerName);
            _uiLog($"[{Personality.Name}] heard {speakerName} say: \"{message}\"{hint}", "c9a9e8");
            Memory.Record("heard", $"{speakerName} said: \"{message}\"{hint}");
            _thoughtLog.Log(Personality.Name, "HEARD", $"{speakerName}: {message}{hint}");
        }

        // Same "delivered once, written to Memory" treatment as heard
        // speech just above — whatever else happened nearby since this
        // NPC's own last turn (someone gathering, giving something
        // away, arriving somewhere, ...) arrives here as one batch, not
        // as it happens. That's what keeps this from turning into a
        // flood of interruptions: nothing here pushes into anyone's
        // context in real time, it just waits in WorldEventLog until
        // whichever NPC's turn comes around asks what it missed — see
        // RecentEventBuffer's own header for the full reasoning.
        foreach ((string actorName, string description) in WorldEventLog.Witnessed(Personality.Name, Actor.GlobalPosition))
        {
            _thoughtLog.Log(Personality.Name, "WITNESSED", description);
            Memory.Record("witnessed", description);
        }

        string perception = BuildPerception();
        bool sleepAllowed = Actor.CanSleep(_world.Home.GlobalPosition);
        var targets = new Mind.AvailableTargets(TreeIds(), FishingSpotIds(), PineTreeIds(), BerryBushIds(), TravelTargetIds(), NearbyNpcNames(), CarriedItems(), sleepAllowed);
        var result = await Mind.Decide(perception, targets, Personality);
        _thinking = false;

        if (!result.Ok && _pureLlmMode)
        {
            // Pure LLM mode: no substitute action, ever. Stand still and
            // retry rather than let a fallback quietly stand in for a
            // decision — the whole point of this mode is that every
            // action seen genuinely came from the model.
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

        var check = SkillCheck.Roll(speakerCharisma.Value, DifficultyClass.OpposedBase + Actor.Stats.CharismaMod);
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

    private string BuildPerception()
    {
        var lines = new List<string> { Personality.DescribeForPrompt() };
        if (_lastResultLine != "")
            lines.Add(_lastResultLine);

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
        for (int i = 0; i < _world.PineTrees.Count; i++)
            if (_world.PineTrees[i].Count > 0)
                options.Add(new GameAction("gather_pinecone", $"pine_{i}", ActionRanges.GatherPinecone));
        for (int i = 0; i < _world.BerryBushes.Count; i++)
            if (_world.BerryBushes[i].Count > 0)
                options.Add(new GameAction("gather_berry", $"berry_{i}", ActionRanges.GatherBerry));

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
            "sleep" => $"{Personality.Name} was asleep nearby for a while.",
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
        double pause = actionId == "wait" ? 1.0 : MinTurnPause;
        await Actor.ToSignal(Actor.GetTree().CreateTimer(pause, processAlways: false), SceneTreeTimer.SignalName.Timeout);

        await TakeTurn();
    }
}
