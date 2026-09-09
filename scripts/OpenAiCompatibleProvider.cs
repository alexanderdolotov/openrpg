using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

// Cloud backend: any OpenAI-compatible /v1/chat/completions endpoint —
// OpenAI itself, Groq, Together, Fireworks, or OpenRouter (which alone
// exposes Claude, GPT, and Llama variants through one key). Response
// shape differs from Ollama in two ways this adapts around: the message
// is nested under choices[0], and tool call "arguments" arrives as a
// JSON-encoded STRING rather than a real object — both get normalized
// into the same ChatMessage/ToolCall shape Ollama produces directly.
public partial class OpenAiCompatibleProvider : Node, ILlmProvider
{
    public string BaseUrl = "https://api.openai.com/v1";
    public string Model = "gpt-4o-mini";
    public string ApiKey = "";
    public float TimeoutSeconds = 60f;

    private HttpRequest _request;

    public override void _Ready()
    {
        _request = new HttpRequest { Timeout = TimeoutSeconds };
        AddChild(_request);
    }

    // Gated through LlmRequestQueue — same shared throttle OllamaProvider
    // routes through, so a cloud backend and a local one never both
    // bypass it just because they're different provider classes.
    public Task<ChatResult> Chat(object[] messages, object[] tools, float temperature = 0.7f) =>
        LlmRequestQueue.Enqueue(() => ChatInternal(messages, tools, temperature));

    private async Task<ChatResult> ChatInternal(object[] messages, object[] tools, float temperature)
    {
        var bodyObj = new Dictionary<string, object>
        {
            ["model"] = Model,
            ["temperature"] = temperature,
            ["messages"] = messages,
        };
        if (tools is { Length: > 0 })
            bodyObj["tools"] = tools;

        string bodyJson = JsonSerializer.Serialize(bodyObj);
        var headers = new List<string> { "Content-Type: application/json" };
        if (!string.IsNullOrEmpty(ApiKey))
            headers.Add($"Authorization: Bearer {ApiKey}");

        Error err = _request.Request($"{BaseUrl.TrimEnd('/')}/chat/completions", headers.ToArray(), HttpClient.Method.Post, bodyJson);
        if (err != Error.Ok)
            return ChatResult.Fail("could_not_start_request");

        Variant[] signalResult = await ToSignal(_request, HttpRequest.SignalName.RequestCompleted);
        long result = signalResult[0].AsInt64();
        long responseCode = signalResult[1].AsInt64();
        byte[] bodyBytes = signalResult[3].AsByteArray();

        if (result != (long)HttpRequest.Result.Success)
            return ChatResult.Fail($"connection_failed_{result}");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(bodyBytes);
        }
        catch (JsonException)
        {
            return ChatResult.Fail("bad_json");
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (responseCode != 200)
            {
                string apiError = root.TryGetProperty("error", out var errEl)
                    ? (errEl.TryGetProperty("message", out var m) ? m.GetString() : errEl.ToString())
                    : $"http_{responseCode}";
                return ChatResult.Fail($"api_error_{apiError}");
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return ChatResult.Fail("bad_message_shape");

            var messageEl = choices[0].GetProperty("message");
            string content = messageEl.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : "";

            var toolCalls = new List<ToolCall>();
            if (messageEl.TryGetProperty("tool_calls", out var tcEl) && tcEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in tcEl.EnumerateArray())
                {
                    if (!tc.TryGetProperty("function", out var fnEl))
                        continue;
                    string name = fnEl.TryGetProperty("name", out var n) ? n.GetString() : "";
                    JsonElement argsElement = ParseArguments(fnEl);
                    toolCalls.Add(new ToolCall { Function = new FunctionCall { Name = name, Arguments = argsElement } });
                }
            }

            return ChatResult.Success(new ChatMessage { Content = content, ToolCalls = toolCalls.ToArray() });
        }
    }

    // OpenAI-compatible APIs send function-call arguments as a JSON
    // string that itself needs parsing, not a nested object like
    // Ollama's native format.
    private static JsonElement ParseArguments(JsonElement fnEl)
    {
        if (!fnEl.TryGetProperty("arguments", out var argsProp) || argsProp.ValueKind != JsonValueKind.String)
            return default;

        string raw = argsProp.GetString();
        if (string.IsNullOrEmpty(raw))
            return default;

        try
        {
            using var argsDoc = JsonDocument.Parse(raw);
            return argsDoc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
