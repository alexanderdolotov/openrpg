using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

// Loaded from mind.local.json at the project root (gitignored — see
// mind.example.json for the shape to copy). Missing file or any parse
// failure falls back to the defaults below (Ollama on analytics.local),
// so the game runs with zero setup unless you actually want to switch
// backends.
public class MindConfig
{
    [JsonPropertyName("provider")] public string Provider { get; set; } = "ollama"; // "ollama" | "openai_compatible"
    [JsonPropertyName("base_url")] public string BaseUrl { get; set; } = "http://analytics.local:11434";
    [JsonPropertyName("model")] public string Model { get; set; } = "llama3.2:3b";
    [JsonPropertyName("api_key")] public string ApiKey { get; set; } = "";
    [JsonPropertyName("log_npc_thoughts")] public bool LogNpcThoughts { get; set; } = false;

    // When true, a genuinely UNREACHABLE backend (the network/provider
    // call itself failing) retries instead of ever substituting
    // RandomFallback() — every action taken under a live connection
    // genuinely came from the LLM. Off by default since that's what a
    // shippable game wants (never stall on a network hiccup); on for
    // testing "does the LLM alone hold up." Does NOT block a fallback
    // for the model responding but failing to produce a usable tool
    // call (bad JSON, an off-menu action, an invalid target) — that's
    // a live, reachable backend having an off attempt, not the
    // connection being down, so it falls back either way (clearly
    // logged as "LLM tool call failed", distinct from "mind
    // unreachable") rather than stalling an NPC indefinitely on a
    // connection that's demonstrably still up. See NpcAgent.
    // IsToolCallFailure for the actual think_/act_-prefix distinction.
    [JsonPropertyName("pure_llm_mode")] public bool PureLlmMode { get; set; } = false;

    // See GameSettings.PermadeathEnabled for what this actually
    // switches — a config-level toggle since it's a "which kind of game
    // is this session" decision, same tier as provider/model, not
    // something that lives on any one character.
    [JsonPropertyName("permadeath_enabled")] public bool PermadeathEnabled { get; set; } = false;

    private const string ConfigPath = "mind.local.json";

    public static MindConfig Load()
    {
        var config = new MindConfig();
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<MindConfig>(json);
                if (loaded != null)
                    config = loaded;
            }
        }
        catch (Exception)
        {
            // malformed config file — run on defaults rather than crash
        }

        // An env var can carry the key instead, so mind.local.json (or
        // even a checked-in mind.json) never has to hold a live secret.
        string envKey = Environment.GetEnvironmentVariable("MIND_API_KEY");
        if (!string.IsNullOrEmpty(envKey))
            config.ApiKey = envKey;

        return config;
    }

    public ILlmProvider CreateProvider()
    {
        if (Provider == "openai_compatible")
        {
            return new OpenAiCompatibleProvider
            {
                BaseUrl = BaseUrl,
                Model = Model,
                ApiKey = ApiKey,
            };
        }
        return new OllamaProvider
        {
            BaseUrl = BaseUrl,
            Model = Model,
        };
    }
}
