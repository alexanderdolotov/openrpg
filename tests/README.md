# Tests

Three layers, split by what each actually needs to run — a plain .NET
process, the real Godot engine, or a live Ollama backend. None of these
existed before the 2026-09-13 NPC-behavior session (rolling action
memory + `listen`/`attentive_listening` + the danger/player-speech/
witnessed-event interrupt watcher); this is that session's own
verification, kept around rather than thrown away.

## `unit/` — plain C#, no Godot engine needed

A real xUnit project (`OpenRpg.Tests`), separate from `OpenRPG.csproj` on
purpose — see its own header comment. It compiles the actual game source
files that have zero Godot **engine** dependency (`Mind.cs`, `NpcMemory.cs`,
a handful of supporting types) directly into its own assembly and drives
them through their real public API (`Mind.Decide`, never the private
`BuildTools`/`BuildAction` directly), using `FakeLlmProvider` in place of
a real backend. Covers: the `listen`/`wait`/gathering tool-gating rules,
the trust-boundary validation in `BuildAction` (an unavailable or
out-of-enum action must fail cleanly, never silently succeed), and
`NpcMemory`'s record/render/compression behavior.

Run:
```
cd tests/unit/OpenRpg.Tests
dotnet test
```

**Not covered here** (and can't be, without the engine running):
`NpcAgent`/`NPCActor` themselves (Godot `Node`s with a real lifecycle),
`RecentEventBuffer`/`SpeechLog`/`WorldEventLog` (use `Time.GetTicksMsec()`,
an engine singleton), `DayNightCycle`. See `gameplay/` for those.

`OpenRPG.csproj` explicitly excludes `tests/unit/**/*.cs` from its own
compile items — without that, the Godot project's implicit glob pulls
this project's files straight into the game build too (duplicate types,
a hard failure on the xunit references it doesn't have).

## `gameplay/` — real Godot Node scripts, run headless

`GameplayTests.cs`, attached to `gameplay_tests.tscn`'s root node, run
through the actual compiled game (unlike `unit/`, these files **do**
compile into `OpenRPG.csproj` — they're real Node scripts, not a separate
assembly). Covers the engine-dependent pieces `unit/` can't reach:
`RecentEventBuffer.HasPending`'s non-consuming-peek contract (the thing
`NpcAgent.ShouldReopenDecision` depends on never silently stealing an
entry `Consume()` would otherwise have delivered), and
`NPCActor.AssignAction`'s reaffirm-no-op guard plus the sleep-visual
cleanup fix (a reopened decision that matches what's already running
must leave it untouched; a genuine switch away from sleep must actually
take effect).

Run headless from the project root (adjust the Godot path for your
install — this was run against `Godot_mono.app`, the Mono/C#-enabled
build, since a plain non-Mono Godot can't load this project at all):
```
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . tests/gameplay/gameplay_tests.tscn
```
Prints `ok:`/`FAIL:` per assertion, `ALL GAMEPLAY TESTS PASSED` or
`N GAMEPLAY TEST(S) FAILED` at the end, and exits 0/1 accordingly — real
pass/fail, unlike `diagnostics/`'s own scratch scripts (see that folder's
README for why those are deliberately print-and-eyeball instead).

**Not covered here**: anything that actually needs a real LLM response
(the interrupt watcher firing end-to-end during a live NPC turn loop,
`listen`/`attentive_listening` actually getting chosen) — that needs
either a real multi-minute play session or `prompts/` below for the
prompt-shape half of it.

## `prompts/` — live-model smoke tests, needs a running Ollama

`run_prompt_tests.py` sends real requests to a local Ollama instance
(reusing `llm_tuning/common.py`'s persona/situation/tool builders, same
fidelity rule that file already follows) for four scenarios exercising
this session's prompt changes: `listen` offered on overheard (non-direct)
speech, a `RECENT ACTIONS` repetition streak, `wait` framed as a last
resort with nothing else obviously to do, and `CURRENTLY: mid-sleep`
reopened by a witnessed event. Not a graded benchmark like
`llm_tuning/5_baseline_eval.py` — there's no single correct answer for
most of these (personality- and temperature-dependent), so it just runs
each scenario a few times and prints what the model actually chose, for
a human to read.

Two of the four scenarios (`RECENT ACTIONS`, `CURRENTLY`) test sections
that aren't wired into `llm_tuning/common.py`'s `build_situation()` yet
(a pre-existing, documented gap — see `ACT_INSTRUCTION`'s own comment
there) — `insert_line()` splices them into the situation text directly
for this one script rather than plumbing new parameters through the
shared harness.

Run (needs Ollama running locally with the model already pulled — see
`llm_tuning/6_export_to_ollama.py`):
```
python3 tests/prompts/run_prompt_tests.py --trials 5
```

**Not run as part of this session's own verification** — no Ollama
instance was reachable in the environment this was built in
(`connection_failed` on `localhost:11434`). The script was still
exercised end-to-end short of the actual network call (tool lists,
personas, and spliced situation text all verified correct), and it fails
with a clear one-line message rather than a traceback when it can't
connect — but the four scenarios' actual model behavior has not been
observed. Worth running for real before trusting the qualitative
"does it switch off a repeated action" / "does it reaffirm sleep"
questions this session's design assumes the model handles reasonably.
