using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenRpg.Tests;

// A scripted ILlmProvider — no network, no Ollama, nothing async actually
// waiting on anything. Every test in this project drives Mind through its
// REAL public API (Decide/DecidePlayerRequest/Summarize), never through
// its private BuildTools/BuildAction/ParseToolCall directly, so this is
// the one seam that needs faking: what a real backend would have said.
//
// Two things worth reading off this after a call: LastTools (the exact
// tools array Mind.BuildTools() produced this call — the seam these
// tests use to assert "listen only appears when ListenAllowed," etc.)
// and CallCount (Mind.Decide's own act-retry loop calls Chat() up to
// twice on a malformed response — tests that care about retry behavior
// can assert on this instead of guessing).
public class FakeLlmProvider : ILlmProvider
{
    private readonly Queue<ChatResult> _responses;

    public object[] LastTools { get; private set; }
    public int CallCount { get; private set; }

    // Every Chat() call (think AND act — Mind.Decide() makes both) draws
    // the next queued response in order. A single response is reused for
    // every remaining call once the queue runs dry — convenient for a
    // test that only cares about the act call and doesn't want to
    // hand-craft a plausible "think" response too.
    private ChatResult _lastForReuse;

    public FakeLlmProvider(params ChatResult[] responses)
    {
        _responses = new Queue<ChatResult>(responses);
    }

    public Task<ChatResult> Chat(object[] messages, object[] tools, float temperature = 0.7f)
    {
        CallCount++;
        if (tools != null)
            LastTools = tools;
        ChatResult result = _responses.Count > 0 ? _responses.Dequeue() : _lastForReuse;
        _lastForReuse = result;
        return Task.FromResult(result);
    }

    // A plain-prose "think" reply — never carries tool_calls, matching
    // what the real think call always returns (see Mind.Decide's own
    // two-call split).
    public static ChatResult Think(string sentence) =>
        ChatResult.Success(new ChatMessage { Content = sentence, ToolCalls = null });

    // A real tool call, arguments included — mirrors exactly what
    // ParseToolCall reads via ExtractField (target_id/emotion/item/
    // amount/message), just built directly rather than round-tripped
    // through JSON text.
    public static ChatResult ToolCall(string name, object args)
    {
        JsonElement parsedArgs = JsonSerializer.SerializeToElement(args);
        return ChatResult.Success(new ChatMessage
        {
            Content = "",
            ToolCalls = new[] { new ToolCall { Function = new FunctionCall { Name = name, Arguments = parsedArgs } } },
        });
    }

    // A response with no tool_calls AND no recognizable action word in
    // its own text — the "no_tool_call" trust-boundary case ParseToolCall
    // falls back to LenientParseFromText for, which also finds nothing.
    public static ChatResult NoToolCall(string proseInstead = "hmm, not sure.") =>
        ChatResult.Success(new ChatMessage { Content = proseInstead, ToolCalls = null });

    public static ChatResult Failure(string error) => ChatResult.Fail(error);
}
