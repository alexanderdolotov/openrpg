using System.Linq;
using System.Threading.Tasks;

namespace OpenRpg.Tests;

// Exercises Mind.BuildTools() indirectly, through the real public
// Decide() call — never reflects into the private method itself. The
// FakeLlmProvider records exactly the tools array Mind actually built
// for the "act" call (LastTools), which is the same array a real
// backend would receive over /api/chat.
public class MindToolGatingTests
{
    private static readonly Personality Persona = new() { Name = "TestNpc", Backstory = "For tests only." };

    private static object GetProp(object obj, string name) =>
        obj.GetType().GetProperty(name)!.GetValue(obj)!;

    private static string[] ToolNames(object[] tools) =>
        tools.Select(t => (string)GetProp(GetProp(t, "function"), "name")).ToArray();

    private static string ToolDescription(object[] tools, string name) =>
        (string)GetProp(GetProp(tools.First(t => (string)GetProp(GetProp(t, "function"), "name") == name), "function"), "description");

    private static async Task<object[]> ToolsFor(Mind.AvailableTargets targets)
    {
        var provider = new FakeLlmProvider(FakeLlmProvider.Think("a plan"), FakeLlmProvider.ToolCall("wait", new { emotion = "neutral" }));
        var mind = new Mind(provider);
        await mind.Decide("SETTING: a test room.", targets, Persona, "STR 10");
        return provider.LastTools;
    }

    [Fact]
    public async Task Listen_NotOffered_WhenNothingWasJustHeard()
    {
        Mind.AvailableTargets targets = TestTargets.Empty();
        targets.ListenAllowed = false;
        object[] tools = await ToolsFor(targets);
        Assert.DoesNotContain("listen", ToolNames(tools));
    }

    [Fact]
    public async Task Listen_Offered_WhenSomethingWasJustHeard()
    {
        Mind.AvailableTargets targets = TestTargets.Empty();
        targets.ListenAllowed = true;
        object[] tools = await ToolsFor(targets);
        Assert.Contains("listen", ToolNames(tools));
    }

    [Fact]
    public async Task Wait_DescriptionFramesItAsALastResort()
    {
        // Regression check for the 2026-09-13 "don't default to idle"
        // change — wait's own tool description is what actually argues
        // against it (see Mind.ActInstruction's header on why a tool's
        // own framing beats prose for a small model), so if this text
        // regresses back to a bare "Do nothing this turn." the whole
        // point of that change is silently gone.
        object[] tools = await ToolsFor(TestTargets.Empty());
        string description = ToolDescription(tools, "wait");
        Assert.Contains("last resort", description);
    }

    [Fact]
    public async Task Sleep_NotOffered_WhenNotAllowed()
    {
        Mind.AvailableTargets targets = TestTargets.Empty();
        targets.SleepAllowed = false;
        object[] tools = await ToolsFor(targets);
        Assert.DoesNotContain("sleep", ToolNames(tools));
    }

    [Fact]
    public async Task Sleep_Offered_WhenAllowed()
    {
        Mind.AvailableTargets targets = TestTargets.Empty();
        targets.SleepAllowed = true;
        object[] tools = await ToolsFor(targets);
        Assert.Contains("sleep", ToolNames(tools));
    }

    [Fact]
    public async Task ResourceTools_OmittedEntirely_WhenNoneInSight()
    {
        // The enum-of-nothing guard (see BuildTools' own header on the
        // 2026-09-08 pick_apple recall regression this fixed) — an empty
        // TreeIds/FishingSpotIds/etc. array must mean the tool is simply
        // absent, not offered with an empty/invalid enum.
        object[] tools = await ToolsFor(TestTargets.Empty());
        string[] names = ToolNames(tools);
        Assert.DoesNotContain("pick_apple", names);
        Assert.DoesNotContain("catch_fish", names);
        Assert.DoesNotContain("attack", names);
        Assert.DoesNotContain("follow", names);
    }
}
