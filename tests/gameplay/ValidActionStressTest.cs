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

    private async Task RunAsync()
    {
        int total = 0, ok = 0;
        var failureReasons = new Dictionary<string, int>();

        void Record(string scenario, int trial, bool resultOk, string error)
        {
            total++;
            if (resultOk) { ok++; return; }
            failureReasons.TryGetValue(error, out int c);
            failureReasons[error] = c + 1;
            GD.Print($"  [{scenario}] trial {trial + 1}: FAILED — {error}");
        }

        // --- Mind.Decide() — the normal, by-far-most-common turn path ---
        foreach ((string name, Personality persona, Mind.AvailableTargets targets, string situation) in DecideScenarios())
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

        // --- Mind.DecidePlayerRequest() — a direct line from the player ---
        {
            const string name = "player_request (asked to pick apples, apple tree in sight)";
            Personality persona = MakePersona("Maren", "A blacksmith's apprentice looking for a quiet place to think.", 0.6f, 0.7f, 0.3f, 0.5f, 0.4f);
            Mind.AvailableTargets targets = FullMenuTargets();
            string situation = BasicSituation(heardLine: "Alex (the real human player, not another character in this world) just said to you: \"can you go pick some apples?\" (this comes across as pretty convincing to you)");
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

        // --- Mind.DecideThreatResponse() — fight/flee/freeze under attack ---
        {
            const string name = "threat_response (a wolf is attacking you right now)";
            Personality persona = MakePersona("Bram", "A blunt, guarded trapper who trusts very few people.", 0.3f, 0.55f, 0.25f, 0.15f, 0.55f);
            string situation = "SETTING: A garden clearing by your home.\n\nNEARBY:\nanimal_5 (wolf): 20 px away — it's coming for YOU, right now!\n\nYOU: carrying nothing; feeling fearful; health 60/100, fatigue 50/100, hunger 50/100";
            int scenarioOk = 0;
            for (int i = 0; i < TrialsPerScenario; i++)
            {
                OllamaProvider provider = NewProvider();
                var mind = new Mind(provider);
                Mind.ThreatResult result = await mind.DecideThreatResponse(situation, persona, selfTargeted: true);
                Record(name, i, result.Ok, result.Error);
                if (result.Ok) scenarioOk++;
                provider.QueueFree();
            }
            GD.Print($"{name}: {scenarioOk}/{TrialsPerScenario} valid actions");
        }

        GD.Print($"\nTOTAL: {ok}/{total} valid actions ({(total > 0 ? 100.0 * ok / total : 0):0.0}%)");
        if (failureReasons.Count > 0)
        {
            GD.Print("Failure reasons:");
            foreach (KeyValuePair<string, int> kv in failureReasons.OrderByDescending(k => k.Value))
                GD.Print($"  {kv.Key}: {kv.Value}");
        }

        GetTree().Quit(0);
    }

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

    // Same section shape NpcAgent.BuildPerception() actually sends
    // (SETTING/ENVIRONMENT/NEARBY/MEMORY/YOU/HEARD) — not maximal
    // fidelity, but close enough that this measures the model against
    // realistically-shaped, realistically-sized context, not a
    // stripped-down toy prompt.
    private static string BasicSituation(string heardLine = null, string recentActionsLine = null)
    {
        var sections = new List<string>
        {
            "SETTING: A garden clearing by your home, beside a winding river, with forest, foothills, and misty mountains to the north.",
        };
        if (recentActionsLine != null)
            sections.Add(recentActionsLine);
        sections.Add(
            "ENVIRONMENT:\n" +
            "home: 220 px away, 4 apples and 1 fish stored there so far\n" +
            "fire pit: 200 px away, near home, not lit right now, no stick or fuel or anything else required, just walk up and light it with the light_fire action whenever you want a fire going.\n" +
            "tree_0 (apple tree): 140 px away, 3 apples ready to pick.\n" +
            "berry_0 (berry bush): 260 px away, 2 berries ready to pick.");
        sections.Add("NEARBY:\nAlex is nearby, 90 px away, feeling neutral.");
        sections.Add("MEMORY:\nYou don't remember anything yet.");
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
}
