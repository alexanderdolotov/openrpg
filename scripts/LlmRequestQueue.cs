using System;
using System.Threading;
using System.Threading.Tasks;

// A shared throttle sitting in front of the real LLM backend — every
// NpcAgent still gets its own Mind and its own ILlmProvider instance
// (see NpcAgent's own header on why: Godot's HttpRequest node can only
// have one request in flight per instance), but nothing previously
// stopped every NPC's own instance from firing at the server
// simultaneously the moment their turns happened to line up.
//
// Measured directly against analytics.local (2026-09-08): VRAM stays
// completely flat under concurrent load — Ollama shares one KV-cache
// pool sized by num_ctx, not one per simultaneous request, so this is
// NOT a VRAM-safety mechanism. It exists because wall-clock latency
// does NOT stay flat: 3 simultaneous requests took 38s total to all
// finish, versus ~8s for one alone — more than 3x, meaning the backend
// serializes concurrent requests to the same model with real per-
// request overhead on top, rather than truly batching them. Static,
// not threaded through every constructor — same pattern as
// WorldRegistry/PathGrid/SpeechLog — since this needs to gate ALL
// NPCs' calls against ONE shared backend, not per-agent state.
public static class LlmRequestQueue
{
    private static readonly object _lock = new();
    private static SemaphoreSlim _gate;
    private static int _capacity = -1;

    // Lazily built from GameSettings.MaxConcurrentLlmRequests on first
    // use rather than a static initializer — GameSettings is set once
    // at boot from MindConfig (see Main._Ready()), which happens before
    // any NpcAgent.Start() ever runs, so by the time this is actually
    // needed the real configured value is already in place. Rebuilt if
    // the setting ever changes between calls (it isn't meant to change
    // mid-session per GameSettings' own header, but this costs nothing
    // to handle correctly regardless). Locked (not just check-then-act)
    // so the very first calls of a session — several NpcAgent.Start()s
    // firing close together — can't each observe `_gate == null` and
    // build their own separate semaphore, which would defeat the whole
    // point: two independent semaphores don't share a slot count, so
    // the real concurrency cap could briefly exceed what's configured.
    private static SemaphoreSlim Gate()
    {
        lock (_lock)
        {
            int wanted = Math.Max(1, GameSettings.MaxConcurrentLlmRequests);
            if (_gate == null || _capacity != wanted)
            {
                _gate = new SemaphoreSlim(wanted, wanted);
                _capacity = wanted;
            }
            return _gate;
        }
    }

    // Called alongside WorldExploration.Reset()/SpeechLog.Reset()/
    // WorldEventLog.Reset() at the top of Main._Ready() (see those for
    // why: static state here survives "Restart Game"'s
    // ReloadCurrentScene() the same way theirs would, since neither is
    // part of the scene tree that reload actually tears down). Without
    // this, an NPC's Chat() call caught mid-flight by a restart never
    // releases its slot: ReloadCurrentScene() frees the HttpRequest node
    // its `await ToSignal(...)` is waiting on, that signal can now never
    // fire, and the `finally { gate.Release(); }` in Enqueue() below
    // never runs for it — a real, permanent leak of one slot per
    // restart-during-a-call, not just a hung Task. Resetting the gate
    // itself (rather than trying to cancel/await the orphaned call) is
    // the simple, correct fix: the old semaphore and whatever's still
    // stuck waiting on it become irrelevant the moment nothing new ever
    // acquires it again, and the fresh one starts at full capacity.
    public static void Reset()
    {
        lock (_lock)
        {
            _gate = null;
            _capacity = -1;
        }
    }

    // Runs `call` once a slot is free, releasing it again whether `call`
    // succeeds or throws — a queued-up NPC turn should never permanently
    // steal a slot because one particular call happened to fail.
    public static async Task<ChatResult> Enqueue(Func<Task<ChatResult>> call)
    {
        SemaphoreSlim gate = Gate();
        await gate.WaitAsync();
        try
        {
            return await call();
        }
        finally
        {
            gate.Release();
        }
    }
}

