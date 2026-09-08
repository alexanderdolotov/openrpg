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

    // The exact (npcId, kind, text) last written, so an immediate
    // repeat (an animal re-logging "still fleeing the same wolf" every
    // physics frame, say) doesn't fill this file with dozens of
    // identical lines a second — see Log()'s own comment. Timestamp is
    // deliberately excluded from the comparison (it always differs);
    // this is about the CONTENT repeating, not the literal line.
    private string _lastNpcId;
    private string _lastKind;
    private string _lastText;

    // Opened once, kept open for the life of the run, instead of
    // File.AppendAllText's own open-write-close every single call. That
    // used to mean a full file-handle open/close syscall for every NPC
    // thought/speech/combat line — cheap on a plain local disk, but this
    // project's logs/ directory lives inside an OneDrive-synced folder
    // (see this session's own earlier brush with a stale-read glitch on
    // Mind.cs from that same sync layer), where each of those opens can
    // stall waiting on the sync client rather than returning instantly.
    // AutoFlush keeps the file as current on disk as AppendAllText's
    // per-call close always was — this only removes the repeated
    // open/close, not the durability.
    private StreamWriter _writer;

    public NpcThoughtLogger(bool enabled)
    {
        _enabled = enabled;
        if (!_enabled)
            return;

        _logPath = Path.Combine(LogDir, $"npc_thoughts_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
        try
        {
            Directory.CreateDirectory(LogDir);
            _writer = new StreamWriter(_logPath, append: true) { AutoFlush = true };
        }
        catch (IOException) { /* best-effort, see Log() */ }
        catch (UnauthorizedAccessException) { /* same */ }
    }

    public void Log(string npcId, string kind, string text)
    {
        if (!_enabled || _writer == null)
            return;

        // "If prev row is exact same, don't log it" — same collapsing
        // rule Main.Log's console output gets, applied here too so the
        // file doesn't fill up with the same repeat even when nobody's
        // watching the live console at all.
        if (npcId == _lastNpcId && kind == _lastKind && text == _lastText)
            return;
        _lastNpcId = npcId;
        _lastKind = kind;
        _lastText = text;

        try
        {
            _writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {npcId} {kind}: {text}");
        }
        catch (IOException)
        {
            // best-effort — never let a debug log failure break the game
        }
        catch (UnauthorizedAccessException)
        {
            // same
        }
        catch (ObjectDisposedException)
        {
            // Close() already ran (Exit button / window close) but an
            // in-flight NPC decision — an async HTTP call already
            // waiting on Ollama when Close() fired — can still resume
            // and call Log() afterward. The _writer==null guard above
            // is the normal path that stops this; this is the belt-
            // and-suspenders fallback for the same reason every other
            // failure here is swallowed instead of thrown.
        }
    }

    // Best-effort close, same posture as Log() itself — a debug log
    // that fails to close cleanly at shutdown still shouldn't be an
    // error anywhere in the caller. Main.cs calls this from every path
    // that ends this run's logger — the Exit button, the OS window's
    // close button, and both restart paths (death, settings-panel
    // Restart), since ReloadCurrentScene() re-runs Main._Ready() and
    // hands _thoughtLog a brand new instance without this call, the
    // previous one's file handle would just leak until GC finalizes it.
    // Nulling _writer (not just disposing it) is what makes Log()'s own
    // `_writer == null` guard correctly skip a call arriving after
    // Close() — safe to call more than once, or with nothing to close.
    public void Close()
    {
        try { _writer?.Dispose(); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        _writer = null;
    }
}
