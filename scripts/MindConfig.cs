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
    // Prefer a bare IP over a .local hostname in mind.local.json if at
    // all possible — confirmed directly on this dev machine
    // (2026-09-08): "analytics.local" pays a real, reproducible ~5.1s
    // tax on EVERY request. Root cause, confirmed with `host
    // analytics.local` (not even an Ollama call): the OS resolver tries
    // real DNS first, gets NXDOMAIN (.local isn't a real DNS domain,
    // only mDNS/Bonjour knows it), and only falls back to mDNS after
    // that failure — every single time, not just once per session.
    // Since Mind.Decide() makes two sequential calls per NPC turn
    // (think + act), that's ~10s of pure dead time per decision before
    // any actual inference even starts, on top of everything else.
    // Switching to the IP directly (see mind.local.json) cut a warm
    // request from ~5.4s wall time down to ~0.4s, matching Ollama's own
    // reported total_duration almost exactly. Only safe if the IP is
    // actually static/reserved, not a floating DHCP lease — this default
    // stays on the hostname since that's true for any given user's LAN,
    // not this one specifically.
    [JsonPropertyName("base_url")] public string BaseUrl { get; set; } = "http://analytics.local:11434";
    [JsonPropertyName("model")] public string Model { get; set; } = "llama3.2:3b";
    [JsonPropertyName("api_key")] public string ApiKey { get; set; } = "";
    [JsonPropertyName("log_npc_thoughts")] public bool LogNpcThoughts { get; set; } = false;

    // Off by default — the whole point of NpcThoughtLogger above is a
    // readable, high-level "what happened" trail; this is the opposite,
    // a low-level "what did we actually send and get back" trail for the
    // specific job of comparing real in-game prompts against
    // llm_tuning's own benchmarked ones (see PromptDebugLogger's own
    // header — a real, motivating case: the deployed fine-tuned model
    // wasn't visibly following instructions any better in actual
    // gameplay despite a clean llm_tuning benchmark number, and there
    // was no way to see the EXACT text a real turn sent without this).
    // Verbose and PII-adjacent enough (full persona/situation/response
    // text every turn) that it stays a separate opt-in from
    // log_npc_thoughts, not folded into it.
    [JsonPropertyName("debug_prompts")] public bool DebugPrompts { get; set; } = false;

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

    // See GameSettings.LogLevel for what this actually gates.
    [JsonPropertyName("log_level")] public int LogLevel { get; set; } = 1;

    // Confirmed directly against analytics.local (2026-09-08): Ollama
    // loads a model with whatever context window IT defaults to, not
    // whatever the model is actually trained for (llama3.2:3b supports
    // 131072 per its own model_info, but was running loaded at 4096 —
    // checked live via /api/ps — since nothing here or in the Modelfile
    // ever asked for more). Once a real prompt (persona + instructions +
    // situation + full tool schema, plus a long session's accumulated
    // memory diary) exceeds whatever's loaded, Ollama does NOT error —
    // it silently drops the front of the context to fit (confirmed via
    // prompt_eval_count pinned exactly at the loaded size regardless of
    // how much longer the actual prompt was), and the model's response
    // degrades to unstructured rambling with no tool call at all in that
    // state — a real, reproduced cause of the exact "talks about it
    // instead of doing it" failure this session spent hours chasing as a
    // pure model-capability problem.
    //
    // First tried 8192 (a straight doubling), but that overflowed the
    // 8GB card's VRAM on analytics.local — `ollama ps` showed it split
    // 22%/78% CPU/GPU instead of 100% GPU, which makes every single NPC
    // decision dramatically slower (CPU-offloaded inference is routinely
    // 5-20x slower than fully-resident GPU). Binary-searching purely for
    // "does ONE request fit in VRAM" found 5632 (comfortably under the
    // measured 5888/6016 single-request tipping point) — but that
    // question turned out to be the wrong one for a multi-NPC game.
    //
    // Tested 3 and 4 truly CONCURRENT requests directly (2026-09-08,
    // this is exactly what LlmRequestQueue's own header describes): at
    // num_ctx=5632, 3 concurrent calls took 38s total to all finish —
    // Ollama was serializing them, not batching them, despite VRAM
    // being fine. At num_ctx=4096, both 3 and 4 concurrent calls
    // finished in ~3-4s total — genuinely parallel, because a smaller
    // per-slot KV-cache reservation leaves room for multiple slots at
    // once. The single-request VRAM ceiling and the concurrent-serving
    // ceiling are two different numbers, and the second one is far more
    // relevant here (see GameSettings.MaxConcurrentLlmRequests). Also
    // confirmed directly against the actual generated training data
    // (llm_tuning/data/train.jsonl) that real content genuinely fits:
    // the single largest real example (most tools offered, longest
    // situation) measured 2976 real tokens via prompt_eval_count —
    // comfortably under 4096 with ~27% headroom, not a tight squeeze.
    // 4096 is thus strictly better than 5632 for this game: same VRAM
    // safety, dramatically better concurrency, still enough room for
    // content. This is specific to analytics.local's actual free VRAM
    // and OLLAMA_NUM_PARALLEL behavior at the time it was measured —
    // if either ever changes (another process using VRAM, a bigger
    // model, more NPCs than this was tested with), re-run the same
    // "fire N concurrent requests, time the total" test from
    // LlmRequestQueue's own header rather than assuming this number
    // still holds.
    [JsonPropertyName("num_ctx")] public int NumCtx { get; set; } = 4096;

    // See GameSettings.MaxConcurrentLlmRequests / LlmRequestQueue for
    // what this actually gates.
    [JsonPropertyName("max_concurrent_llm_requests")] public int MaxConcurrentLlmRequests { get; set; } = 4;

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
        catch (Exception e)
        {
            // Malformed config file — run on defaults rather than crash, but
            // NOT silently: a real, reproduced case (2026-09-08) — a stray
            // "//" comment line (invalid in standard JSON; System.Text.Json
            // rejects it by default, unlike Newtonsoft or JSON5) made
            // JsonSerializer.Deserialize throw, and this catch swallowed it
            // completely, so mind.local.json's entire model/base_url/
            // logging config silently reverted to hardcoded defaults with
            // zero indication anything was wrong — indistinguishable from
            // "the model just isn't following the new config" without
            // actually reading this file's own JSON syntax by hand. GD.
            // PrintErr (not GD.Print) so it stands out in the console the
            // same way any other real startup problem would.
            Godot.GD.PrintErr($"mind.local.json failed to parse ({e.Message}) — running on defaults instead of your config. Check its JSON syntax (no comments allowed).");
        }

        // An env var can carry the key instead, so mind.local.json (or
        // even a checked-in mind.json) never has to hold a live secret.
        string envKey = Environment.GetEnvironmentVariable("MIND_API_KEY");
        if (!string.IsNullOrEmpty(envKey))
            config.ApiKey = envKey;

        return config;
    }

    // The Settings panel's Ollama-address field is the one thing in
    // here meant to be changed from inside the game rather than by
    // hand-editing the file — this is what makes that change stick
    // across a restart (see Main.BuildSettingsMenu). Deliberately does
    // NOT just serialize `this` as-is: if MIND_API_KEY is set, Load()
    // above already overwrote this object's own ApiKey with it in
    // memory (exactly so a live secret never needs to live in the
    // file) — blindly saving `this` back out would defeat that by
    // writing the env var's own secret INTO the file the next time
    // anyone touches an unrelated setting. Only ApiKey gets this
    // special handling; every other field just round-trips normally.
    // Returns whether the write actually succeeded — this project lives
    // in a OneDrive-synced folder (see this codebase's own build
    // history for prior transient file-read/build glitches from that),
    // so a momentary lock or sync conflict on mind.local.json here is a
    // real possibility, not just a hypothetical. Load() above already
    // swallows exactly this class of I/O failure and falls back to
    // defaults; Save() does the same rather than letting the exception
    // propagate into whatever UI handler called it (see
    // Main.BuildSettingsMenu's restart button — Save() runs before the
    // unpause/reload, so an uncaught exception here would leave the
    // player stuck on a frozen, paused screen instead of just losing
    // the one setting change).
    public bool Save()
    {
        string envKey = Environment.GetEnvironmentVariable("MIND_API_KEY");
        bool apiKeyCameFromEnv = !string.IsNullOrEmpty(envKey) && ApiKey == envKey;
        var snapshot = new MindConfig
        {
            Provider = Provider, BaseUrl = BaseUrl, Model = Model,
            ApiKey = apiKeyCameFromEnv ? "" : ApiKey,
            LogNpcThoughts = LogNpcThoughts, DebugPrompts = DebugPrompts, PureLlmMode = PureLlmMode,
            PermadeathEnabled = PermadeathEnabled, LogLevel = LogLevel,
            NumCtx = NumCtx, MaxConcurrentLlmRequests = MaxConcurrentLlmRequests,
        };
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(snapshot, options));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Every NPC still gets its own provider instance — see NpcAgent.
    // Initialize()'s own AddChild((Node)provider): both providers are
    // real Godot Nodes (they each own an HttpRequest child), not just
    // ILlmProvider implementations, so a plain-class decorator can't
    // wrap one here the way QueuedLlmProvider first tried to — that
    // broke NpcFactory.Create's own Node cast outright. LlmRequestQueue
    // gating instead lives INSIDE each provider's own Chat() (see
    // OllamaProvider/OpenAiCompatibleProvider), so every instance still
    // funnels through the one shared queue without needing to itself be
    // wrapped in anything.
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
            NumCtx = NumCtx,
        };
    }
}
