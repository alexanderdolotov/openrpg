using System;
using System.IO;

// Plain-text audit trail of NPC thoughts and actions, opt-in via
// mind.local.json's "log_npc_thoughts" — off by default so nobody gets
// a growing file they didn't ask for. Written under logs/, not the
// project root, so debug output stays out of the way of everything
// else there (and the whole directory is one gitignore line).
//
// One file per run, stamped with when the run started, rather than
// every session appending to the same npc_thoughts.log — that's what
// actually makes two runs comparable side by side instead of one long
// file with no seam between sessions.
//
// Best-effort: a write failure (disk full, permissions, whatever) is
// swallowed rather than crashing the game over a debug log — the same
// posture as everything else here that touches the outside world.
public class NpcThoughtLogger
{
    private const string LogDir = "logs";

    private readonly bool _enabled;
    private readonly string _logPath;

    public string LogPath => _logPath;

    public NpcThoughtLogger(bool enabled)
    {
        _enabled = enabled;
        if (!_enabled)
            return;

        _logPath = Path.Combine(LogDir, $"npc_thoughts_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
        try
        {
            Directory.CreateDirectory(LogDir);
        }
        catch (IOException) { /* best-effort, see Log() */ }
        catch (UnauthorizedAccessException) { /* same */ }
    }

    public void Log(string npcId, string kind, string text)
    {
        if (!_enabled)
            return;

        try
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {npcId} {kind}: {text}";
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
        catch (IOException)
        {
            // best-effort — never let a debug log failure break the game
        }
        catch (UnauthorizedAccessException)
        {
            // same
        }
    }
}
