using System.Text.Json;
using System.Text.Json.Serialization;

// The normalized shape every ILlmProvider must translate its own wire
// format into. Ollama's response already matches this almost exactly;
// OpenAI-compatible APIs nest under choices[0].message and encode
// "arguments" as a JSON string instead of an object — each provider
// absorbs its own differences here so Mind never has to know which
// backend answered.

public class ChatMessage
{
    [JsonPropertyName("content")] public string Content { get; set; }
    [JsonPropertyName("tool_calls")] public ToolCall[] ToolCalls { get; set; }
}

public class ToolCall
{
    [JsonPropertyName("function")] public FunctionCall Function { get; set; }
}

public class FunctionCall
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("arguments")] public JsonElement Arguments { get; set; }
}

public readonly struct ChatResult
{
    public readonly bool Ok;
    public readonly string Error;
    public readonly ChatMessage Message;

    private ChatResult(bool ok, string error, ChatMessage message)
    {
        Ok = ok;
        Error = error;
        Message = message;
    }

    public static ChatResult Fail(string error) => new(false, error, null);
    public static ChatResult Success(ChatMessage message) => new(true, null, message);
}
