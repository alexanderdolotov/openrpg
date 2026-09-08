using Godot;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

// Local backend: a self-hosted Ollama instance, reachable over LAN or
// loopback. Ollama's /api/chat response already matches ChatMessage's
// shape almost exactly (tool_calls[].function.arguments comes back as
// a real JSON object, not a string) — no adapting needed beyond
// unwrapping "message" and checking for an "error" field.
public partial class OllamaProvider : Node, ILlmProvider
{
    public string BaseUrl = "http://analytics.local:11434";
    public string Model = "llama3.2:3b";
    public float TimeoutSeconds = 60f; // first call pays a cold model-load cost on a fresh host
    // See MindConfig.NumCtx's own comment — without this, Ollama loads
    // the model at whatever ITS OWN default context window is (confirmed
    // 4096 on analytics.local, nothing to do with what the model can
    // actually handle), and silently truncates the front of any prompt
    // that exceeds it rather than erroring.
    public int NumCtx = 8192;

    private HttpRequest _request;

    public override void _Ready()
    {
        _request = new HttpRequest { Timeout = TimeoutSeconds };
        AddChild(_request);
    }

    public async Task<ChatResult> Chat(object[] messages, object[] tools, float temperature = 0.7f)
    {
        var bodyObj = new System.Collections.Generic.Dictionary<string, object>
        {
            ["model"] = Model,
            ["stream"] = false,
            ["keep_alive"] = "30m",
            ["options"] = new { temperature, num_ctx = NumCtx },
            ["messages"] = messages,
        };
        if (tools is { Length: > 0 })
            bodyObj["tools"] = tools;

        string bodyJson = JsonSerializer.Serialize(bodyObj);
        string[] headers = { "Content-Type: application/json" };

        Error err = _request.Request($"{BaseUrl.TrimEnd('/')}/api/chat", headers, HttpClient.Method.Post, bodyJson);
        if (err != Error.Ok)
            return ChatResult.Fail("could_not_start_request");

        Variant[] signalResult = await ToSignal(_request, HttpRequest.SignalName.RequestCompleted);
        long result = signalResult[0].AsInt64();
        long responseCode = signalResult[1].AsInt64();
        byte[] bodyBytes = signalResult[3].AsByteArray();

        if (result != (long)HttpRequest.Result.Success)
            return ChatResult.Fail($"connection_failed_{result}");
        if (responseCode != 200)
            return ChatResult.Fail($"http_{responseCode}");

        OllamaChatResponse parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<OllamaChatResponse>(Encoding.UTF8.GetString(bodyBytes));
        }
        catch (JsonException)
        {
            return ChatResult.Fail("bad_json");
        }

        if (parsed == null)
            return ChatResult.Fail("bad_json");
        if (!string.IsNullOrEmpty(parsed.Error))
            // Ollama can return HTTP 200 with an error payload (wrong
            // model name, server mid-reload, ...) instead of an HTTP
            // error code.
            return ChatResult.Fail($"api_error_{parsed.Error}");
        if (parsed.Message == null)
            return ChatResult.Fail("bad_message_shape");

        return ChatResult.Success(parsed.Message);
    }

    private class OllamaChatResponse
    {
        [JsonPropertyName("message")] public ChatMessage Message { get; set; }
        [JsonPropertyName("error")] public string Error { get; set; }
    }
}
