using Godot;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

// TEMPORARY — isolated test harness for verifying LlmRequestQueue's
// concurrency gate with synthetic delayed calls, without loading the
// full Main.tscn scene (which spawns real NPCs that immediately start
// making their own real LLM calls through the same shared static
// queue, contaminating any timing measurement taken against it).
// Delete this file once LlmRequestQueue is verified.
public partial class QueueTestHarness : Node
{
    private Godot.Collections.Array<float> _results;
    private bool _done;

    public void StartTest(int count, int delayMs)
    {
        _done = false;
        _results = null;
        _ = RunTest(count, delayMs);
    }

    public bool IsDone() => _done;
    public Godot.Collections.Array<float> GetResults() => _results;

    private async Task RunTest(int count, int delayMs)
    {
        var sw = Stopwatch.StartNew();
        var tasks = new List<Task<(float, float)>>();
        for (int i = 0; i < count; i++)
            tasks.Add(TimedEnqueue(sw, delayMs));
        (float, float)[] results = await Task.WhenAll(tasks);
        var flat = new Godot.Collections.Array<float>();
        foreach ((float s, float e) in results) { flat.Add(s); flat.Add(e); }
        _results = flat;
        _done = true;
    }

    private static async Task<(float, float)> TimedEnqueue(Stopwatch sw, int delayMs)
    {
        float start = 0, end = 0;
        await LlmRequestQueue.Enqueue(async () =>
        {
            start = sw.ElapsedMilliseconds;
            await Task.Delay(delayMs);
            end = sw.ElapsedMilliseconds;
            return ChatResult.Success(null);
        });
        return (start, end);
    }
}
