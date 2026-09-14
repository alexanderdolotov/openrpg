using Godot;

// Headless gameplay checks for the pieces added this session that
// actually need the real engine running (RecentEventBuffer uses
// Time.GetTicksMsec(); NPCActor is a live Node with a real _Ready()/
// state machine) — see tests/README.md for why these live here instead
// of in tests/unit/OpenRpg.Tests, which can't touch either. Run via:
//
//   Godot --headless --path . tests/gameplay/gameplay_tests.tscn
//
// No external test framework — a plain pass/fail script matching
// diagnostics/'s own existing headless-SceneTree convention, just with
// real assertions instead of print-and-eyeball. Exits 0 on success,
// non-zero (and PrintErr's every failure) otherwise, so this is
// scriptable (CI, a pre-commit hook) unlike diagnostics/'s own scripts.
public partial class GameplayTests : Node
{
    private int _failures;

    public override void _Ready()
    {
        TestRecentEventBufferHasPendingDoesNotConsume();
        TestAssignActionReaffirmIsANoOp();
        TestAssignActionGenuineSwitchAwayFromSleep();

        if (_failures == 0)
            GD.Print("ALL GAMEPLAY TESTS PASSED");
        else
            GD.PrintErr($"{_failures} GAMEPLAY TEST(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(bool condition, string description)
    {
        if (condition) { GD.Print($"  ok: {description}"); return; }
        _failures++;
        GD.PrintErr($"  FAIL: {description}");
    }

    // The core new capability this session's interrupt watcher
    // (NpcAgent.ShouldReopenDecision) depends on: peeking must never
    // steal an entry a real Consume() call still needs a moment later.
    private void TestRecentEventBufferHasPendingDoesNotConsume()
    {
        GD.Print("RecentEventBuffer.HasPending...");
        var buffer = new RecentEventBuffer(30f);
        buffer.Record("Wren", new Vector2(0, 0), "Wren picks an apple.");

        Check(buffer.HasPending("Maren", new Vector2(10, 10), 260f), "HasPending sees a fresh entry in range");
        var consumed = buffer.Consume("Maren", new Vector2(10, 10), 260f);
        Check(consumed.Count == 1, "Consume() still finds it after HasPending merely peeked at it");
        Check(!buffer.HasPending("Maren", new Vector2(10, 10), 260f), "HasPending is false once actually consumed");
    }

    // NPCActor.AssignAction's reaffirm-no-op guard — a reopened decision
    // that lands on the exact same action+target already in progress
    // must leave CurrentAction (and, implicitly, _elapsed/_waypoints/the
    // sleep timer) completely untouched, not overwrite it with a new,
    // otherwise-identical GameAction instance.
    private void TestAssignActionReaffirmIsANoOp()
    {
        GD.Print("NPCActor.AssignAction reaffirm-no-op...");
        var actor = new NPCActor { Name = "TestActorReaffirm" };
        AddChild(actor); // synchronous _Ready() — this node is already in the tree (see NpcFactory's own comment on the same call)

        var first = new GameAction("sleep", "", 0f);
        actor.AssignAction(first);
        Check(ReferenceEquals(actor.CurrentAction, first), "sleep assigned for the first time takes effect");
        Check(actor.IsMidLongAction, "sleeping counts as a long action in progress");

        // A DIFFERENT GameAction instance with the same id+target — the
        // real shape of what a reopened decision that reaffirms sleep
        // actually produces every time (Mind.BuildAction always returns
        // a fresh instance).
        var reaffirm = new GameAction("sleep", "", 0f);
        actor.AssignAction(reaffirm);
        Check(ReferenceEquals(actor.CurrentAction, first), "reaffirming the same in-progress action is a true no-op");

        actor.QueueFree();
    }

    // The other half — a GENUINE switch away from sleep must still take
    // effect (the no-op guard must not over-fire) and must correctly
    // leave the Sleeping state, which is what actually exercises this
    // session's other fix: EndSleepVisual() now runs on that transition
    // instead of only from ReceiveDamage/HandleIncapacitation.
    private void TestAssignActionGenuineSwitchAwayFromSleep()
    {
        GD.Print("NPCActor.AssignAction genuine switch away from sleep...");
        var actor = new NPCActor { Name = "TestActorWakeUp" };
        AddChild(actor);

        actor.AssignAction(new GameAction("sleep", "", 0f));
        Check(actor.IsMidLongAction, "asleep");

        var wakeUp = new GameAction("wait", "", 0f);
        actor.AssignAction(wakeUp);
        Check(ReferenceEquals(actor.CurrentAction, wakeUp), "a genuinely different action really does take effect");
        Check(!actor.IsMidLongAction, "no longer mid-long-action once switched away from sleep");

        actor.QueueFree();
    }
}
