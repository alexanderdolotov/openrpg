namespace OpenRpg.Tests;

public class NpcMemoryTests
{
    [Fact]
    public void Render_WithNothingRecorded_ReturnsPlaceholder()
    {
        var memory = new NpcMemory();
        Assert.Equal("You don't remember anything yet.", memory.Render());
    }

    [Fact]
    public void Record_ShowsUpUnderRecently()
    {
        var memory = new NpcMemory();
        memory.Record("action", "picked an apple");
        string rendered = memory.Render();
        Assert.Contains("Recently:", rendered);
        Assert.Contains("picked an apple", rendered);
    }

    [Fact]
    public void NeedsCompression_FalseUnderTheCharBudget()
    {
        var memory = new NpcMemory();
        memory.Record("action", "a short entry");
        Assert.False(memory.NeedsCompression);
    }

    [Fact]
    public void NeedsCompression_TrueOnceRawLogExceedsMaxLlmChars()
    {
        var memory = new NpcMemory();
        // NpcMemory.MaxLlmChars is 1200 — comfortably exceeded by a
        // handful of realistic-length entries.
        for (int i = 0; i < 30; i++)
            memory.Record("action", $"did something moderately descriptive, attempt number {i}");
        Assert.True(memory.NeedsCompression);
    }

    [Fact]
    public async Task CompressIfNeeded_SetsDiaryAndClearsRawEntries()
    {
        var memory = new NpcMemory();
        for (int i = 0; i < 30; i++)
            memory.Record("action", $"did something moderately descriptive, attempt number {i}");
        Assert.True(memory.NeedsCompression);

        var provider = new FakeLlmProvider(FakeLlmProvider.Think("A short diary paragraph about the day."));
        var mind = new Mind(provider);

        await memory.CompressIfNeeded(mind);

        Assert.False(memory.NeedsCompression);
        Assert.Equal("A short diary paragraph about the day.", memory.Diary);
        string rendered = memory.Render();
        Assert.Contains("What you remember overall:", rendered);
        Assert.DoesNotContain("Recently:", rendered); // raw entries were cleared
    }

    [Fact]
    public async Task CompressIfNeeded_IsANoOp_WhenBelowThreshold()
    {
        var memory = new NpcMemory();
        memory.Record("action", "one short entry");

        // A provider that would fail the test if actually called —
        // CompressIfNeeded must return before ever reaching Mind.Summarize.
        var provider = new FakeLlmProvider(FakeLlmProvider.Failure("should never be called"));
        var mind = new Mind(provider);

        await memory.CompressIfNeeded(mind);

        Assert.Equal(0, provider.CallCount);
        Assert.Equal("", memory.Diary);
    }
}
