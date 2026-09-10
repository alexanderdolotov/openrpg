using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// Low-level "what did we actually send, and what came back" trail —
// opt-in via mind.local.json's "debug_prompts" (see MindConfig.
// DebugPrompts' own header for why this exists as a separate log from
// NpcThoughtLogger's higher-level one). Motivating case: a deployed
// fine-tuned model wasn't visibly following instructions any better in
// real gameplay despite a clean llm_tuning benchmark number, and there
// was no way to see the EXACT system/user text (and raw response) a real
// turn sent without this — llm_tuning/5_baseline_eval.py already
// replicates the request shape faithfully for offline eval, but nothing
// captured what the LIVE game was actually sending turn to turn.
//
// Static, not instance-based like NpcThoughtLogger — Mind.cs (where
// every one of these calls actually happens, right where the full
// system/user text and the provider's raw response are already in
// scope) has no reference to Main or NpcAgent to hand an instance
// through, and threading one in would mean changing Decide()/
// DecidePlayerRequest()/DecideThreatResponse()'s signatures and every
// call site for a debug-only feature. Same "decoupled, not threaded
// through every constructor" shape as GameSettings/WorldRegistry/
// SpeechLog already use for session-wide state.
//
// Same open-once-keep-open StreamWriter pattern as NpcThoughtLogger, same
// reasons (this project's logs/ directory is OneDrive-synced; repeated
// open/close per line can stall on the sync client) — see that class's
// own header for the full explanation. Best-effort throughout: a debug
// log failing to write is never a reason to break the game.
public static class PromptDebugLogger
{
    // Own subdirectory, not flat alongside npc_thoughts_*.log — these
    // files are much larger (full system+user text every turn, not
    // NpcThoughtLogger's one-liners) and serve a different job (prompt
    // forensics, not a play-session narrative); keeping them visually and
    // physically separate makes it obvious at a glance which kind of log
    // a given file is, and means "just tail the play narrative" never
    // means scrolling past multi-KB prompt dumps to get there.
    private const string LogDir = "logs/prompt_debug";

    private static StreamWriter _writer;
    private static bool _enabled;

    // Called once from Main._Ready(), mirroring how NpcThoughtLogger gets
    // constructed there — see this class's own header for why this is a
    // static Init() instead of a constructor. Never writes to the
    // console/game terminal (no GD.Print anywhere in this file) — file
    // only, by design, since a full prompt dump on every turn would
    // drown out everything else there.
    public static void Init(bool enabled)
    {
        _enabled = enabled;
        if (!_enabled)
            return;

        string logPath = Path.Combine(LogDir, $"prompt_debug_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
        try
        {
            Directory.CreateDirectory(LogDir);
            _writer = new StreamWriter(logPath, append: true) { AutoFlush = true };
        }
        catch (IOException) { /* best-effort, see Log() */ }
        catch (UnauthorizedAccessException) { /* same */ }
    }

    // callKind: "think" / "act" / "player_request" / "threat_self" /
    // "threat_ally" — which of Mind's own call shapes this was, so the
    // log reads as a real turn-by-turn trace, not an undifferentiated
    // dump. toolNames: just the names offered this call, not the full
    // JSON schema — that's static/derivable from Mind.BuildTools() given
    // the same AvailableTargets, and repeating full tool descriptions on
    // every single turn would make this file far less readable for what
    // it's actually for (comparing real prompts against llm_tuning's
    // benchmarked ones, spotting why a specific turn went wrong) without
    // adding information a reader doesn't already have another way to
    // get. Full system/user text always logged in full, unabridged — the
    // one thing this file exists to capture faithfully.
    public static void Log(string npcName, string callKind, string systemText, string userText,
        IEnumerable<string> toolNames, ChatResult result)
    {
        if (!_enabled || _writer == null)
            return;

        var sb = new StringBuilder();
        sb.AppendLine($"=== [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {npcName} — {callKind} ===");
        sb.AppendLine("--- SYSTEM ---");
        sb.AppendLine(systemText);
        sb.AppendLine("--- USER ---");
        sb.AppendLine(userText);
        sb.AppendLine($"--- TOOLS OFFERED: {string.Join(", ", toolNames)} ---");
        sb.AppendLine("--- RESPONSE ---");
        if (!result.Ok)
        {
            sb.AppendLine($"(provider error: {result.Error})");
        }
        else
        {
            string content = result.Message?.Content ?? "";
            if (content != "")
                sb.AppendLine($"content: {content}");
            ToolCall[] toolCalls = result.Message?.ToolCalls;
            if (toolCalls is { Length: > 0 })
            {
                foreach (ToolCall tc in toolCalls)
                    sb.AppendLine($"tool_call: {tc.Function?.Name}({tc.Function?.Arguments.ToString()})");
            }
            else if (content == "")
            {
                sb.AppendLine("(empty content, no tool_calls)");
            }
        }
        sb.AppendLine();

        try
        {
            _writer.Write(sb.ToString());
        }
        catch (IOException) { /* best-effort, see NpcThoughtLogger's own Log() */ }
        catch (UnauthorizedAccessException) { }
        catch (ObjectDisposedException) { /* Close() already ran on an in-flight call — same race NpcThoughtLogger guards against */ }
    }

    // Same "Main.cs calls this from every path that ends the run" wiring
    // as NpcThoughtLogger.Close() — see that method's own header. Safe to
    // call more than once, or with nothing open.
    public static void Close()
    {
        try { _writer?.Dispose(); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        _writer = null;
    }
}
