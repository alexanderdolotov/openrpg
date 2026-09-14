using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// "Does the model always return something the game will actually
// accept" — stress-tests the REAL trust boundary (Mind.Decide/
// DecidePlayerRequest/DecideThreatResponse -> ParseToolCall ->
// BuildAction), against a REAL Ollama backend, using the exact same
// Mind.cs and OllamaProvider.cs a live NPC does. Not a fake provider
// (see tests/unit's own FakeLlmProvider-based tests for the
// deterministic trust-boundary checks) — this measures the model's
// actual real-world reliability, which nothing else in tests/ answers.
//
// A MindResult with Ok==false here does NOT necessarily mean an NPC
// gets stuck — NpcAgent.RandomFallback() (or the LenientParseFromText
// recovery already inside ParseToolCall) covers most of these in real
// play, per pure_llm_mode being off by default. This specifically
// measures how often that safety net is even needed, which is exactly
// the question "does llama always return a valid action" is really
// asking.
//
// Four sections, in order: general "does it return something valid"
// scenarios (DecideScenarios), item-IDENTIFICATION scenarios (does it
// pick a real, listed id — never an invented one — when several targets
// of the same kind are in sight, the everyday case the real map
// actually looks like), INTERRUPT scenarios (a decision reopened
// mid-action, via the CURRENTLY line — does it weigh finishing
// against the reason it was reopened, or exploratory-only for now,
// see NpcAgent.ShouldReopenDecision), and two scenarios lifted
// VERBATIM from real prompt_debug/*.log captures (see RealLogScenarios'
// own header) rather than hand-written text, to catch anything a
// synthetic scenario's own phrasing might paper over.
//
// Run headless (needs a real Ollama URL/model reachable from wherever
// this runs — see mind.local.json for this project's own configured
// one, currently http://192.168.1.145:11434):
//   Godot --headless --path . tests/gameplay/valid_action_stress_test.tscn
public partial class ValidActionStressTest : Node
{
    [Export] public string BaseUrl = "http://192.168.1.145:11434";
    [Export] public string Model = "openrpg-npc_ep4a-llama3.2-3b:latest";
    [Export] public int TrialsPerScenario = 8;

    private const string StatsLine = "STR 10 DEX 10 CON 10 INT 10 WIS 10 CHA 10 BRV 10";

    private int _total, _ok;
    private readonly Dictionary<string, int> _failureReasons = new();

    public override void _Ready()
    {
        _ = RunAsync();
    }

    private OllamaProvider NewProvider()
    {
        var provider = new OllamaProvider { BaseUrl = BaseUrl, Model = Model };
        AddChild(provider); // _Ready() wires up its HttpRequest child — needs to be in the tree first
        return provider;
    }

    // Every graded call in this file goes through here — one place that
    // counts totals/failures and frees the provider, regardless of
    // which of the three Mind entry points was actually exercised.
    private void Record(string scenario, int trial, bool resultOk, string error)
    {
        _total++;
        if (resultOk) { _ok++; return; }
        _failureReasons.TryGetValue(error, out int c);
        _failureReasons[error] = c + 1;
        GD.Print($"  [{scenario}] trial {trial + 1}: FAILED — {error}");
    }

    private async Task RunAsync()
    {
        // --- Section 1: general "does it return something valid" ---
        foreach ((string name, Personality persona, Mind.AvailableTargets targets, string situation) in DecideScenarios())
            await RunDecideGraded(name, persona, targets, situation);

        {
            const string name = "player_request (asked to pick apples, apple tree in sight)";
            Personality persona = MakePersona("Maren", "A blacksmith's apprentice looking for a quiet place to think.", 0.6f, 0.7f, 0.3f, 0.5f, 0.4f);
            Mind.AvailableTargets targets = FullMenuTargets();
            string situation = BasicSituation(heardLine: "Alex (the real human player, not another character in this world) just said to you: \"can you go pick some apples?\" (this comes across as pretty convincing to you)");
            await RunPlayerRequestGraded(name, persona, targets, situation);
        }

        {
            const string name = "threat_response (a wolf is attacking you right now)";
            Personality persona = MakePersona("Bram", "A blunt, guarded trapper who trusts very few people.", 0.3f, 0.55f, 0.25f, 0.15f, 0.55f);
            string situation = "SETTING: A garden clearing by your home.\n\nNEARBY:\nanimal_5 (wolf): 20 px away — it's coming for YOU, right now!\n\nYOU: carrying nothing; feeling fearful; health 60/100, fatigue 50/100, hunger 50/100";
            await RunThreatGraded(name, persona, situation, selfTargeted: true);
        }

        // --- Section 2: item identification — several same-kind targets
        // in sight (the everyday shape of a real, explored map, not a
        // toy one-of-each), asked generically for one of them. Graded on
        // two things: did it call the right TOOL, and is the target_id
        // it used actually one of the ones really offered (never an
        // invented one) — that second check is the real point, since
        // BuildAction's own trust boundary already guarantees it
        // structurally, but a live end-to-end check against the real
        // model costs nothing and catches a regression in that
        // boundary itself, not just in the model's behavior.
        foreach ((string name, string toolName, string[] validIds, Personality persona, Mind.AvailableTargets targets, string situation) in ItemIdentificationScenarios())
            await RunItemIdentificationGraded(name, toolName, validIds, persona, targets, situation);

        // --- Section 3: interrupt scenarios — a decision reopened
        // mid-action via CURRENTLY (see NpcAgent.ShouldReopenDecision).
        // No single "correct" answer here (whether finishing or
        // switching is right depends on judgment a benchmark can't
        // grade), so these are exploratory: every trial's actual choice
        // gets printed, plus a same-vs-different tally, for a human to
        // read — the earlier "0/20 ever reaffirmed sleep" finding this
        // session came from exactly this kind of run.
        foreach ((string name, string inProgressAction, Personality persona, Mind.AvailableTargets targets, string situation) in InterruptScenarios())
            await RunInterruptExploratory(name, inProgressAction, persona, targets, situation);

        // --- Section 4: verbatim real-log scenarios ---
        await RunRealLogScenarios();

        GD.Print($"\nTOTAL (graded scenarios only): {_ok}/{_total} valid actions ({(_total > 0 ? 100.0 * _ok / _total : 0):0.0}%)");
        if (_failureReasons.Count > 0)
        {
            GD.Print("Failure reasons:");
            foreach (KeyValuePair<string, int> kv in _failureReasons.OrderByDescending(k => k.Value))
                GD.Print($"  {kv.Key}: {kv.Value}");
        }

        GetTree().Quit(0);
    }

    // --- Section 1 runners ---

    private async Task RunDecideGraded(string name, Personality persona, Mind.AvailableTargets targets, string situation)
    {
        int scenarioOk = 0;
        for (int i = 0; i < TrialsPerScenario; i++)
        {
            OllamaProvider provider = NewProvider();
            var mind = new Mind(provider);
            Mind.MindResult result = await mind.Decide(situation, targets, persona, StatsLine);
            Record(name, i, result.Ok, result.Error);
            if (result.Ok) scenarioOk++;
            provider.QueueFree();
        }
        GD.Print($"{name}: {scenarioOk}/{TrialsPerScenario} valid actions");
    }

    private async Task RunPlayerRequestGraded(string name, Personality persona, Mind.AvailableTargets targets, string situation)
    {
        int scenarioOk = 0;
        for (int i = 0; i < TrialsPerScenario; i++)
        {
            OllamaProvider provider = NewProvider();
            var mind = new Mind(provider);
            Mind.MindResult result = await mind.DecidePlayerRequest(situation, targets, persona, StatsLine);
            Record(name, i, result.Ok, result.Error);
            if (result.Ok) scenarioOk++;
            provider.QueueFree();
        }
        GD.Print($"{name}: {scenarioOk}/{TrialsPerScenario} valid actions");
    }

    private async Task RunThreatGraded(string name, Personality persona, string situation, bool selfTargeted)
    {
        int scenarioOk = 0;
        for (int i = 0; i < TrialsPerScenario; i++)
        {
            OllamaProvider provider = NewProvider();
            var mind = new Mind(provider);
            Mind.ThreatResult result = await mind.DecideThreatResponse(situation, persona, selfTargeted);
            Record(name, i, result.Ok, result.Error);
            if (result.Ok) scenarioOk++;
            provider.QueueFree();
        }
        GD.Print($"{name}: {scenarioOk}/{TrialsPerScenario} valid actions");
    }

    // --- Section 2 runner ---

    private async Task RunItemIdentificationGraded(string name, string expectedTool, string[] validIds, Personality persona, Mind.AvailableTargets targets, string situation)
    {
        int scenarioOk = 0, toolMatched = 0, targetValid = 0;
        for (int i = 0; i < TrialsPerScenario; i++)
        {
            OllamaProvider provider = NewProvider();
            var mind = new Mind(provider);
            Mind.MindResult result = await mind.DecidePlayerRequest(situation, targets, persona, StatsLine);
            Record(name, i, result.Ok, result.Error);
            if (result.Ok)
            {
                scenarioOk++;
                if (result.Action.Id == expectedTool) toolMatched++;
                // Only meaningful for the expected gathering tool — a
                // declined/unrelated reply legitimately has no target
                // to check, and shouldn't count against this.
                if (result.Action.Id != expectedTool || System.Array.IndexOf(validIds, result.Action.TargetId) >= 0)
                    targetValid++;
                else
                    GD.Print($"  [{name}] trial {i + 1}: called {expectedTool} with target \"{result.Action.TargetId}\" — NOT one of the ids actually offered!");
            }
            provider.QueueFree();
        }
        GD.Print($"{name}: {scenarioOk}/{TrialsPerScenario} valid, {toolMatched}/{TrialsPerScenario} called {expectedTool}, {targetValid}/{TrialsPerScenario} used a real listed id");
    }

    // --- Section 3 runner ---

    private async Task RunInterruptExploratory(string name, string inProgressAction, Personality persona, Mind.AvailableTargets targets, string situation)
    {
        var choices = new Dictionary<string, int>();
        int sameCount = 0, differentCount = 0, failCount = 0;
        for (int i = 0; i < TrialsPerScenario; i++)
        {
            OllamaProvider provider = NewProvider();
            var mind = new Mind(provider);
            Mind.MindResult result = await mind.Decide(situation, targets, persona, StatsLine);
            if (!result.Ok)
            {
                failCount++;
                GD.Print($"  [{name}] trial {i + 1}: FAILED — {result.Error}");
            }
            else
            {
                string label = result.Action.TargetId != "" ? $"{result.Action.Id} -> {result.Action.TargetId}" : result.Action.Id;
                choices.TryGetValue(label, out int c);
                choices[label] = c + 1;
                if (result.Action.Id == inProgressAction) sameCount++; else differentCount++;
                GD.Print($"  [{name}] trial {i + 1}: {label}");
            }
            provider.QueueFree();
        }
        GD.Print($"{name}: kept doing {inProgressAction} {sameCount}/{TrialsPerScenario}, switched {differentCount}/{TrialsPerScenario}, failed {failCount}/{TrialsPerScenario}");
        GD.Print($"  choice breakdown: {string.Join(", ", choices.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }

    // --- Personas / targets shared across sections ---

    private static Personality MakePersona(string name, string backstory, float o, float c, float e, float a, float n) =>
        new() { Name = name, Backstory = backstory, Openness = o, Conscientiousness = c, Extraversion = e, Agreeableness = a, Neuroticism = n };

    private static Mind.AvailableTargets FullMenuTargets() => new()
    {
        TreeIds = new[] { "tree_0", "tree_1" },
        FishingSpotIds = new[] { "fish_0" },
        PineTreeIds = new[] { "pine_0" },
        BerryBushIds = new[] { "berry_0" },
        TravelTargetIds = new[] { "misty_mountains" },
        NearbyNpcNames = new[] { "Alex" },
        CarriedItems = System.Array.Empty<string>(),
        AnimalIds = System.Array.Empty<string>(),
        StickIds = System.Array.Empty<string>(),
        SleepAllowed = false,
        EatAllowed = false,
        LightFireAllowed = true,
        MakeTorchAllowed = false,
        CookMeatAllowed = false,
        ListenAllowed = false,
    };

    private static Mind.AvailableTargets SparseMenuTargets() => new()
    {
        TreeIds = System.Array.Empty<string>(),
        FishingSpotIds = System.Array.Empty<string>(),
        PineTreeIds = System.Array.Empty<string>(),
        BerryBushIds = System.Array.Empty<string>(),
        TravelTargetIds = System.Array.Empty<string>(),
        NearbyNpcNames = new[] { "Alex" },
        CarriedItems = System.Array.Empty<string>(),
        AnimalIds = System.Array.Empty<string>(),
        StickIds = System.Array.Empty<string>(),
        SleepAllowed = false,
        EatAllowed = false,
        LightFireAllowed = true,
        MakeTorchAllowed = false,
        CookMeatAllowed = false,
        ListenAllowed = false,
    };

    // A real, explored map has SEVERAL of each resource type, not one —
    // exactly the shape 19-00-55's own captured "lets go fishing guys"
    // player_request turn actually offered (4 trees, 4 fishing spots, 3
    // pine trees, 6 berry bushes; reproduced in ItemIdentificationScenarios
    // below and again verbatim in RealLogScenarios).
    private static Mind.AvailableTargets ManyOfEachTargets() => new()
    {
        TreeIds = new[] { "tree_0", "tree_2", "tree_1", "tree_3" }, // nearest-first, matching NpcAgent.TreeIds()' own sort
        FishingSpotIds = new[] { "fish_2", "fish_1", "fish_3", "fish_0" },
        PineTreeIds = new[] { "pine_0", "pine_1", "pine_2" },
        BerryBushIds = new[] { "berry_2", "berry_5", "berry_3", "berry_0", "berry_1", "berry_4" },
        TravelTargetIds = new[] { "misty_mountains" },
        NearbyNpcNames = new[] { "Maren", "Finn", "Alex" },
        CarriedItems = System.Array.Empty<string>(),
        AnimalIds = System.Array.Empty<string>(),
        StickIds = new[] { "stick_0", "stick_1", "stick_2" },
        SleepAllowed = false,
        EatAllowed = false,
        LightFireAllowed = true,
        MakeTorchAllowed = false,
        CookMeatAllowed = false,
        ListenAllowed = false,
    };

    // Same section shape NpcAgent.BuildPerception() actually sends
    // (SETTING/[CURRENTLY]/[RECENT ACTIONS]/ENVIRONMENT/NEARBY/MEMORY/YOU/HEARD)
    // — not maximal fidelity, but close enough that this measures the
    // model against realistically-shaped, realistically-sized context,
    // not a stripped-down toy prompt.
    private static string BasicSituation(string heardLine = null, string recentActionsLine = null, string currentlyLine = null,
        string environment = null, string nearby = null, string memory = null)
    {
        var sections = new List<string>
        {
            "SETTING: A garden clearing by your home, beside a winding river, with forest, foothills, and misty mountains to the north.",
        };
        if (currentlyLine != null)
            sections.Add($"CURRENTLY: {currentlyLine}");
        if (recentActionsLine != null)
            sections.Add(recentActionsLine);
        sections.Add(environment ??
            "ENVIRONMENT:\n" +
            "home: 220 px away, 4 apples and 1 fish stored there so far\n" +
            "fire pit: 200 px away, near home, not lit right now, no stick or fuel or anything else required, just walk up and light it with the light_fire action whenever you want a fire going.\n" +
            "tree_0 (apple tree): 140 px away, 3 apples ready to pick.\n" +
            "berry_0 (berry bush): 260 px away, 2 berries ready to pick.");
        sections.Add(nearby ?? "NEARBY:\nAlex is nearby, 90 px away, feeling neutral.");
        sections.Add(memory ?? "MEMORY:\nYou don't remember anything yet.");
        sections.Add("YOU: carrying nothing; feeling neutral; health 90/100, fatigue 70/100, hunger 70/100");
        if (heardLine != null)
            sections.Add($"HEARD:\n{heardLine}");
        return string.Join("\n\n", sections);
    }

    private IEnumerable<(string, Personality, Mind.AvailableTargets, string)> DecideScenarios()
    {
        yield return (
            "decide (full menu, nothing pressing)",
            MakePersona("Maren", "A blacksmith's apprentice looking for a quiet place to think.", 0.6f, 0.7f, 0.3f, 0.5f, 0.4f),
            FullMenuTargets(),
            BasicSituation());

        yield return (
            "decide (full menu, overheard ambient speech from another NPC)",
            MakePersona("Finn", "Grew up on a fishing boat and feels most himself with his feet in the water.", 0.7f, 0.4f, 0.6f, 0.6f, 0.3f),
            FullMenuTargets(),
            BasicSituation(heardLine: "Wren just said to you: \"I think I'll head to the river later.\" (this comes across as pretty convincing to you)"));

        yield return (
            "decide (sparse menu — only deposit/wait/speak/follow/steal/light_fire)",
            MakePersona("Bram", "A blunt, guarded trapper who trusts very few people.", 0.3f, 0.55f, 0.25f, 0.15f, 0.55f),
            SparseMenuTargets(),
            BasicSituation());

        yield return (
            "decide (RECENT ACTIONS streak — pick_apple x3)",
            MakePersona("Finn", "Grew up on a fishing boat and feels most himself with his feet in the water.", 0.7f, 0.4f, 0.6f, 0.6f, 0.3f),
            FullMenuTargets(),
            BasicSituation(recentActionsLine: "RECENT ACTIONS (yours, oldest to newest): pick_apple, pick_apple, pick_apple — that's \"pick_apple\" 3 times in a row now."));
    }

    // --- Section 2: item identification ---

    private IEnumerable<(string, string, string[], Personality, Mind.AvailableTargets, string)> ItemIdentificationScenarios()
    {
        Personality wren = MakePersona("Wren", "Has never stayed anywhere long. Every hazy shape on the horizon is an open question.", 0.9f, 0.25f, 0.55f, 0.5f, 0.2f);
        Mind.AvailableTargets targets = ManyOfEachTargets();

        yield return (
            "item ID (4 trees in sight, asked to pick apples)",
            "pick_apple", targets.TreeIds, wren, targets,
            BasicSituation(heardLine: "Alex (the real human player, not another character in this world) just said to you: \"can you go pick some apples?\" (this comes across as pretty convincing to you)"));

        yield return (
            "item ID (4 fishing spots in sight, asked to catch a fish)",
            "catch_fish", targets.FishingSpotIds, wren, targets,
            BasicSituation(heardLine: "Alex (the real human player, not another character in this world) just said to you: \"can you catch a fish for me?\" (this comes across as pretty convincing to you)"));

        yield return (
            "item ID (6 berry bushes in sight, asked to gather berries)",
            "gather_berry", targets.BerryBushIds, wren, targets,
            BasicSituation(heardLine: "Alex (the real human player, not another character in this world) just said to you: \"go grab some berries?\" (this comes across as pretty convincing to you)"));

        yield return (
            "item ID (3 sticks on the ground, asked generically for one)",
            "pick_up_stick", targets.StickIds, wren, targets,
            BasicSituation(heardLine: "Alex (the real human player, not another character in this world) just said to you: \"grab a stick for me?\" (this comes across as pretty convincing to you)"));
    }

    // --- Section 3: interrupt scenarios (exploratory) ---

    private IEnumerable<(string, string, Personality, Mind.AvailableTargets, string)> InterruptScenarios()
    {
        Personality wren = MakePersona("Wren", "Has never stayed anywhere long. Every hazy shape on the horizon is an open question.", 0.9f, 0.25f, 0.55f, 0.5f, 0.2f);

        // NOTE: no mid-sleep scenario here on purpose, even though one
        // used to exist — sleep is now excluded from the reopen path
        // entirely (see NpcAgent.ShouldReopenDecision's own header), so
        // CURRENTLY can never actually show "sleep" in the real game
        // anymore. A hand-built CURRENTLY-mid-sleep prompt would test a
        // state the game itself can no longer construct.

        // Mid-travel, reopened by a newly-alert-worthy animal — the
        // "road becomes scarier" case this whole mechanism was
        // motivated by.
        {
            Mind.AvailableTargets targets = FullMenuTargets();
            yield return (
                "interrupt (mid-travel to misty_mountains, a wolf just came into view)",
                "travel", wren, targets,
                BasicSituation(
                    currentlyLine: "You're still in the middle of travel -> misty_mountains — keep going unless something below actually changes your mind.",
                    nearby: "NEARBY:\nanimal_7 (wolf): 90 px away — a dangerous animal, not attacking anyone right now, but worth being careful around."));
        }

        // Mid-gathering-walk, reopened because the player spoke
        // directly — this specific combination (CURRENTLY + a genuine
        // player line) is what NpcAgent.TakeTurn() actually builds
        // before routing into HandleDirectPlayerRequest, so the
        // "graded" player-request path is exercised again here, just
        // with CURRENTLY present this time.
        {
            Mind.AvailableTargets targets = FullMenuTargets();
            yield return (
                "interrupt (mid pick_apple walk, player calls out directly)",
                "pick_apple", wren, targets,
                BasicSituation(
                    currentlyLine: "You're still in the middle of pick_apple -> tree_0 — keep going unless something below actually changes your mind.",
                    heardLine: "Alex (the real human player, not another character in this world) just said to you: \"wait, come here a sec\" (this comes across as pretty convincing to you)"));
        }
    }

    // --- Section 4: verbatim real-log scenarios ---
    //
    // Lifted character-for-character (aside from re-wrapping long lines)
    // from logs/prompt_debug/prompt_debug_2026-09-13_19-00-55.log — the
    // exact session a real play-test flagged ("it took 2 attempts to ask
    // to go fishing and only Wren came"). Testing against a synthesized
    // approximation of that turn risks smoothing over exactly the
    // phrasing/formatting quirk that mattered; this is the literal
    // request, target list, and memory state the model actually saw.
    private async Task RunRealLogScenarios()
    {
        Personality wren = MakePersona("Wren", "Has never stayed anywhere long. Every hazy shape on the horizon is an open question, and Wren has never been good at leaving those alone.", 0.9f, 0.25f, 0.55f, 0.5f, 0.2f);
        const string wrenStats = "STR 9 DEX 11 CON 12 INT 9 WIS 10 CHA 7 BRV 14";

        // Verbatim from prompt_debug_2026-09-13_19-00-55.log:509 — the
        // exact "lets go fishing guys" turn from the flagged session.
        Mind.AvailableTargets fishingTargets = new()
        {
            TreeIds = new[] { "tree_0", "tree_2", "tree_1", "tree_3" },
            FishingSpotIds = new[] { "fish_2", "fish_1", "fish_3", "fish_0" },
            PineTreeIds = new[] { "pine_0", "pine_1", "pine_2" },
            BerryBushIds = new[] { "berry_2", "berry_5", "berry_3", "berry_0", "berry_1", "berry_4" },
            TravelTargetIds = new[] { "misty_mountains" },
            NearbyNpcNames = new[] { "Maren", "Finn", "Alex" },
            CarriedItems = System.Array.Empty<string>(),
            AnimalIds = System.Array.Empty<string>(),
            StickIds = new[] { "stick_0", "stick_1", "stick_2" }, // discovered per the log's own turns 17-19; not shown in NEARBY's own budgeted text but pick_up_stick WAS offered
            SleepAllowed = false,
            EatAllowed = false,
            LightFireAllowed = true,
            MakeTorchAllowed = false,
            CookMeatAllowed = false,
            ListenAllowed = false,
        };
        string fishingSituation =
            "SETTING: A garden clearing by your home, beside a winding river, with forest, foothills, and misty mountains to the north.\n\n" +
            "ENVIRONMENT:\n" +
            "home: 44 px away, 0 apples and 0 fish stored there so far\n" +
            "fire pit: 44 px away, near home, not lit right now — nothing is needed to light it, no stick or fuel or anything else required, just walk up and light it with the light_fire action whenever you want a fire going.\n" +
            "misty_mountains: a place you've actually been before, 830 px away.\n" +
            "berry_2: 3 berries left, 176 px away\n" +
            "pine_0: 3 pinecones left, 206 px away\n" +
            "berry_5: 3 berries left, 220 px away\n" +
            "fish_2: 3 fish left, 393 px away\n" +
            "berry_3: 3 berries left, 439 px away\n" +
            "tree_0: 3 apples left, 452 px away\n" +
            "fish_1: 3 fish left, 520 px away\n" +
            "tree_2: 3 apples left, 523 px away\n" +
            "berry_0: 3 berries left, 530 px away\n" +
            "tree_1: 3 apples left, 635 px away\n" +
            "berry_1: 3 berries left, 698 px away\n" +
            "pine_1: 3 pinecones left, 762 px away\n" +
            "berry_4: 3 berries left, 780 px away\n" +
            "tree_3: 3 apples left, 875 px away\n" +
            "fish_3: 3 fish left, 892 px away\n" +
            "fish_0: 3 fish left, 943 px away\n" +
            "pine_2: 3 pinecones left, 957 px away\n\n" +
            "NEARBY:\n" +
            "Maren is nearby, 72 px away, feeling neutral.\n" +
            "Finn is nearby, 80 px away, feeling neutral.\n" +
            "Alex is nearby, 206 px away, feeling neutral.\n\n" +
            "MEMORY:\nNo notable memories yet.\n\n" +
            "YOU: carrying nothing; feeling neutral; health 100/100, fatigue 98/100 (well-rested), hunger 99/100 (well-fed)\n\n" +
            "HEARD:\n" +
            "Alex (the real human player, not another character in this world) just said to you: \"lets go fishing guys\" (this comes across as pretty convincing to you)";

        {
            const string name = "REAL LOG: \"lets go fishing guys\" (the exact flagged turn)";
            int scenarioOk = 0, calledCatchFish = 0;
            for (int i = 0; i < TrialsPerScenario; i++)
            {
                OllamaProvider provider = NewProvider();
                var mind = new Mind(provider);
                Mind.MindResult result = await mind.DecidePlayerRequest(fishingSituation, fishingTargets, wren, wrenStats);
                Record(name, i, result.Ok, result.Error);
                if (result.Ok)
                {
                    scenarioOk++;
                    if (result.Action.Id == "catch_fish") calledCatchFish++;
                    GD.Print($"  [{name}] trial {i + 1}: {result.Action.Id}" + (result.Action.TargetId != "" ? $" -> {result.Action.TargetId}" : "") + (result.Action.Message != "" ? $" \"{result.Action.Message}\"" : ""));
                }
                provider.QueueFree();
            }
            GD.Print($"{name}: {scenarioOk}/{TrialsPerScenario} valid, {calledCatchFish}/{TrialsPerScenario} actually called catch_fish");
        }

        // Verbatim from prompt_debug_2026-09-13_14-52-17.log:6079 — a
        // real threat_ally turn (a friend, not this NPC, under attack).
        {
            const string name = "REAL LOG: threat_ally (a wolf is attacking Alex, not you)";
            string threatSituation =
                "SETTING: A garden clearing by your home, beside a winding river, with forest, foothills, and misty mountains to the north.\n\n" +
                "LAST RESULT: Your last action (speak) succeeded.\n\n" +
                "RECENT ACTIONS (yours, oldest to newest): travel, speak, attack, wait, attack, speak\n\n" +
                "ENVIRONMENT:\n" +
                "home: 679 px away, 0 apples and 0 fish stored there so far\n" +
                "fire pit: 732 px away, near home, burning right now — a stick can be lit from it to make a torch, and raw rabbit meat can be cooked over it.\n" +
                "misty_mountains: a place you've actually been before, 170 px away.\n\n" +
                "NEARBY:\n" +
                "Alex is nearby, 51 px away, feeling neutral.\n" +
                "animal_5 (wolf): 48 px away — it's attacking Alex right now!\n\n" +
                "MEMORY:\nNo notable memories yet.\n\n" +
                "YOU: carrying 1 stick; feeling curious; health 100/100, fatigue 60/100, hunger 70/100";
            int scenarioOk = 0;
            var choices = new Dictionary<string, int>();
            for (int i = 0; i < TrialsPerScenario; i++)
            {
                OllamaProvider provider = NewProvider();
                var mind = new Mind(provider);
                Mind.ThreatResult result = await mind.DecideThreatResponse(threatSituation, wren, selfTargeted: false);
                Record(name, i, result.Ok, result.Error);
                if (result.Ok)
                {
                    scenarioOk++;
                    choices.TryGetValue(result.Choice, out int c);
                    choices[result.Choice] = c + 1;
                }
                provider.QueueFree();
            }
            GD.Print($"{name}: {scenarioOk}/{TrialsPerScenario} valid — {string.Join(", ", choices.Select(kv => $"{kv.Key}={kv.Value}"))}");
        }
    }
}
