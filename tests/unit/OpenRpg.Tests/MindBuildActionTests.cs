namespace OpenRpg.Tests;

// Exercises the trust boundary Mind.ParseToolCall/BuildAction actually
// enforce, through the real public Decide() call — an unrecognized
// action name, an out-of-enum target, or a conditionally-gated action
// called anyway must all fail cleanly rather than silently succeed.
public class MindBuildActionTests
{
    private static readonly Personality Persona = new() { Name = "TestNpc", Backstory = "For tests only." };

    [Fact]
    public async Task Listen_WhenNotAllowed_FailsEvenIfModelCallsItAnyway()
    {
        // The tool schema already omits "listen" when !ListenAllowed (see
        // BuildTools), but BuildAction is the REAL trust boundary — a
        // model that calls it anyway (hallucinated, or a stale tool
        // definition cached client-side) must still be rejected, not
        // silently allowed through.
        var provider = new FakeLlmProvider(
            FakeLlmProvider.Think("I'll just listen."),
            FakeLlmProvider.ToolCall("listen", new { emotion = "curious" }));
        var mind = new Mind(provider);

        Mind.AvailableTargets targets = TestTargets.Empty();
        targets.ListenAllowed = false;

        Mind.MindResult result = await mind.Decide("SETTING: a test room.", targets, Persona, "STR 10");

        Assert.False(result.Ok);
        Assert.Equal("listen_not_available", result.Error);
    }

    [Fact]
    public async Task Listen_WhenAllowed_Succeeds()
    {
        var provider = new FakeLlmProvider(
            FakeLlmProvider.Think("I'll just listen."),
            FakeLlmProvider.ToolCall("listen", new { emotion = "curious" }));
        var mind = new Mind(provider);

        Mind.AvailableTargets targets = TestTargets.Empty();
        targets.ListenAllowed = true;

        Mind.MindResult result = await mind.Decide("SETTING: a test room.", targets, Persona, "STR 10");

        Assert.True(result.Ok);
        Assert.Equal("listen", result.Action.Id);
        Assert.Equal("", result.Action.TargetId);
    }

    [Fact]
    public async Task Wait_FallsThroughToDefaultCase_AndSucceeds()
    {
        // "wait" has no explicit case in BuildAction's switch — it's the
        // one action that relies on hitting `default:`, so this is the
        // regression test for that specific branch, not just "wait
        // generally works."
        var provider = new FakeLlmProvider(
            FakeLlmProvider.Think("Nothing else to do."),
            FakeLlmProvider.ToolCall("wait", new { emotion = "neutral" }));
        var mind = new Mind(provider);

        Mind.MindResult result = await mind.Decide("SETTING: a test room.", TestTargets.Empty(), Persona, "STR 10");

        Assert.True(result.Ok);
        Assert.Equal("wait", result.Action.Id);
    }

    [Fact]
    public async Task PickApple_WithWrongTreeId_RecoversToNearestRealTree()
    {
        // 2026-09-13: a live-model stress test caught the real backend
        // naming a plausible-looking but wrong tree id — BuildAction now
        // defaults to the nearest REAL one (TreeIds' own first entry,
        // sorted nearest-to-this-NPC) instead of failing the whole
        // action outright, same treatment an OMITTED target already got.
        var provider = new FakeLlmProvider(
            FakeLlmProvider.Think("I'll grab an apple."),
            FakeLlmProvider.ToolCall("pick_apple", new { target_id = "tree_99", emotion = "content" }));
        var mind = new Mind(provider);

        Mind.AvailableTargets targets = TestTargets.Empty();
        targets.TreeIds = new[] { "tree_0", "tree_1" }; // "tree_99" is not one of these

        Mind.MindResult result = await mind.Decide("SETTING: a test room.", targets, Persona, "STR 10");

        Assert.True(result.Ok);
        Assert.Equal("pick_apple", result.Action.Id);
        Assert.Equal("tree_0", result.Action.TargetId); // the nearest real one, not the invented one
    }

    [Fact]
    public async Task PickApple_WithNoTreesAtAll_StillFails()
    {
        // The recovery above only helps when there's something real to
        // recover TO — a genuinely empty TreeIds means this is a real
        // "not available" case, not a wrong-id typo, and must still fail.
        var provider = new FakeLlmProvider(
            FakeLlmProvider.Think("I'll grab an apple."),
            FakeLlmProvider.ToolCall("pick_apple", new { target_id = "tree_99", emotion = "content" }));
        var mind = new Mind(provider);

        Mind.AvailableTargets targets = TestTargets.Empty(); // TreeIds is empty

        Mind.MindResult result = await mind.Decide("SETTING: a test room.", targets, Persona, "STR 10");

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task Attack_WithTargetNotInSight_Fails()
    {
        var provider = new FakeLlmProvider(
            FakeLlmProvider.Think("I'll fight it."),
            FakeLlmProvider.ToolCall("attack", new { target_id = "animal_999", emotion = "angry" }));
        var mind = new Mind(provider);

        Mind.AvailableTargets targets = TestTargets.Empty();
        targets.AnimalIds = new[] { "animal_1" }; // "animal_999" is not one of these

        Mind.MindResult result = await mind.Decide("SETTING: a test room.", targets, Persona, "STR 10");

        Assert.False(result.Ok);
        Assert.Equal("invalid_target_animal_999", result.Error);
    }

    [Fact]
    public async Task Attack_WithRealNearbyTarget_Succeeds()
    {
        var provider = new FakeLlmProvider(
            FakeLlmProvider.Think("I'll fight it."),
            FakeLlmProvider.ToolCall("attack", new { target_id = "animal_1", emotion = "angry" }));
        var mind = new Mind(provider);

        Mind.AvailableTargets targets = TestTargets.Empty();
        targets.AnimalIds = new[] { "animal_1" };

        Mind.MindResult result = await mind.Decide("SETTING: a test room.", targets, Persona, "STR 10");

        Assert.True(result.Ok);
        Assert.Equal("animal_1", result.Action.TargetId);
    }

    [Fact]
    public async Task UnknownToolName_FailsWithUnknownAction()
    {
        var provider = new FakeLlmProvider(
            FakeLlmProvider.Think("I'll frobnicate."),
            FakeLlmProvider.ToolCall("frobnicate", new { emotion = "neutral" }));
        var mind = new Mind(provider);

        Mind.MindResult result = await mind.Decide("SETTING: a test room.", TestTargets.Empty(), Persona, "STR 10");

        Assert.False(result.Ok);
        Assert.Equal("unknown_action_frobnicate", result.Error);
    }

    [Fact]
    public async Task NoToolCallAndNoRecognizableWord_FailsWithNoToolCall()
    {
        var provider = new FakeLlmProvider(
            FakeLlmProvider.Think("Hmm."),
            FakeLlmProvider.NoToolCall("I'm not really sure what to make of any of this."));
        var mind = new Mind(provider);

        Mind.MindResult result = await mind.Decide("SETTING: a test room.", TestTargets.Empty(), Persona, "STR 10");

        Assert.False(result.Ok);
        Assert.Equal("no_tool_call", result.Error);
    }
}
