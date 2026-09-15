using Xunit;

namespace OpenRpg.Tests;

// Mind.SanitizeSpokenMessage sits between whatever a "speak" tool call
// produced and the player/other NPCs actually hearing it — see its own
// header for the two distinct real failure modes it exists to catch:
// raw JSON scaffolding leaking through as if it were prose
// (StillLooksLikeJson), and a genuinely well-formed tool call whose
// "message" argument itself comes back cut off mid-word
// (LooksTruncatedMidWord — the 2026-09-14 "NPCs say 'I don' a lot"
// finding, confirmed recurring across real logs/prompt_debug/*.log runs
// from 2026-09-09 through 2026-09-14).
public class MindSanitizeSpokenMessageTests
{
    [Fact]
    public void OrdinaryCompleteSentence_PassesThroughUnchanged()
    {
        Assert.Equal("I don't see any apple trees near me.", Mind.SanitizeSpokenMessage("I don't see any apple trees near me."));
    }

    [Theory]
    [InlineData("I don")]
    [InlineData("Yeah, I didn")]
    [InlineData("She wasn")]
    [InlineData("I can")]
    public void TruncatedMidContraction_IsRejected(string truncated)
    {
        Assert.Null(Mind.SanitizeSpokenMessage(truncated));
    }

    [Fact]
    public void TruncatedBeforeNestedQuote_IsRejected()
    {
        // The real observed shape: the model started a nested quotation
        // ("Wren said, \"...\"") and the whole quoted part never arrived.
        Assert.Null(Mind.SanitizeSpokenMessage("Wren said, "));
    }

    [Fact]
    public void MalformedToolCallJsonLeak_ExtractsJustTheMessage()
    {
        // The original 2026-09-10 case this method was built for — see
        // its own header.
        string raw = "{\"name\":\"speak\",\"parameters\":{\"message\":\"I got 2 apples\",\"emotion\":\"neutral\"}}";
        Assert.Equal("I got 2 apples", Mind.SanitizeSpokenMessage(raw));
    }

    [Fact]
    public void StillLooksLikeRawJson_IsRejected()
    {
        Assert.Null(Mind.SanitizeSpokenMessage("{\"name\":\"speak\"}"));
    }

    [Fact]
    public void NullInput_ReturnsNull()
    {
        Assert.Null(Mind.SanitizeSpokenMessage(null));
    }

    [Fact]
    public void EmptyOrWhitespaceInput_ReturnsNullNotRawEmptyString()
    {
        // Used to return the empty/whitespace raw string as-is, which
        // handed the player silent, missing dialogue — StillLooksLikeJson
        // was already built to treat blank text as "still broken," but an
        // early IsNullOrWhiteSpace-guard short-circuited past it before
        // it ever got a chance to run. null here is what lets
        // NpcAgent.OnActionCompleted fall through to CleanUpBrokenSpeech,
        // which resolves an empty raw line straight to its placeholder,
        // synchronously, no LLM call spent on nothing.
        Assert.Null(Mind.SanitizeSpokenMessage(""));
        Assert.Null(Mind.SanitizeSpokenMessage("   "));
    }
}
