# Tests

Three layers, split by what each actually needs to run — a plain .NET
process, the real Godot engine, or a live Ollama backend. None of these
existed before the 2026-09-13 NPC-behavior session (rolling action
memory + `listen`/`attentive_listening` + the danger/player-speech/
witnessed-event interrupt watcher, plus a real overlapping-`TakeTurn()`
race that watcher introduced and a target-id recovery fix in
`Mind.BuildAction`); this is that session's own verification, kept
around rather than thrown away.

## `unit/` — plain C#, no Godot engine needed

A real xUnit project (`OpenRpg.Tests`), separate from `OpenRPG.csproj` on
purpose — see its own header comment. It compiles the actual game source
files that have zero Godot **engine** dependency (`Mind.cs`, `NpcMemory.cs`,
a handful of supporting types) directly into its own assembly and drives
them through their real public API (`Mind.Decide`, never the private
`BuildTools`/`BuildAction` directly), using `FakeLlmProvider` in place of
a real backend. Covers: the `listen`/`wait`/gathering tool-gating rules,
the trust-boundary validation in `BuildAction` (an unavailable or
out-of-enum action must fail cleanly, never silently succeed; an invalid
but recoverable target id defaults to the nearest real one), and
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
a hard failure on the xunit references it doesn't have). `tests/gameplay/`
is deliberately NOT excluded — see that section.

## `gameplay/` — real Godot Node scripts, run headless

Two scripts, each paired with its own `.tscn` (their root node's script)
— unlike `tests/unit/`, these files **do** compile into `OpenRPG.csproj`
(they're real Node scripts a scene can attach, not a separate assembly).

**`GameplayTests.cs` / `gameplay_tests.tscn`** — engine-dependent unit-style
checks, no LLM involved: `RecentEventBuffer.HasPending`'s non-consuming-peek
contract (the thing `NpcAgent.ShouldReopenDecision` depends on never
silently stealing an entry `Consume()` would otherwise have delivered);
`NPCActor.AssignAction`'s reaffirm-no-op guard plus the sleep-visual
cleanup fix (a reopened decision that matches what's already running must
leave it untouched; a genuine switch away from sleep must actually take
effect); and `IsMidLongAction` correctly excluding `Attempting` — the
exact boundary a real bug lived in (the interrupt watcher originally
gated only on `_thinking`, which goes false the instant the LLM responds,
well before the chosen action's own `Attempting` window finishes —
letting it fire a second, fully overlapping `TakeTurn()` call mid-action;
confirmed from a live session's own out-of-order thought-log lines, fixed
by gating on `IsMidLongAction` too).

**`ValidActionStressTest.cs` / `valid_action_stress_test.tscn`** — a
live-model stress test against a REAL Ollama backend (not a fake),
through the real `Mind.Decide`/`DecidePlayerRequest`/`DecideThreatResponse`
entry points. Four sections:
1. General "does it return something valid" scenarios (full menu, sparse
   menu, ambient overheard speech, a RECENT ACTIONS streak).
2. **Item identification** — several same-kind targets in sight at once
   (several trees/fishing spots/berry bushes/sticks, the everyday shape
   of a real explored map, not a toy one-of-each), asked generically for
   one. Graded on calling the right tool AND using a real listed id,
   never an invented one.
3. **Interrupt scenarios** (exploratory, no single correct answer) — a
   decision reopened mid-action via `CURRENTLY`: mid-travel + a
   newly-sighted animal, mid-gather-walk + the player calling out
   directly. (No mid-sleep case — sleep is excluded from the reopen path
   entirely now, see the note below.) Every trial's actual choice prints,
   plus a kept-vs-switched tally.
4. Two scenarios lifted **verbatim** from real `logs/prompt_debug/*.log`
   captures — including the exact "lets go fishing guys" turn a real
   play session flagged — rather than hand-written approximations, so a
   synthetic scenario's own phrasing can't paper over what actually
   mattered.

Run headless from the project root (adjust the Godot path for your
install — this was run against `Godot_mono.app`, the Mono/C#-enabled
build, since a plain non-Mono Godot can't load this project at all):
```
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . tests/gameplay/gameplay_tests.tscn
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . tests/gameplay/valid_action_stress_test.tscn
```
`GameplayTests` prints `ok:`/`FAIL:` per assertion and exits 0/1 — real
pass/fail, unlike `diagnostics/`'s own scratch scripts (see that folder's
README for why those are deliberately print-and-eyeball instead).
`ValidActionStressTest` grades sections 1-2 and 4 (a `TOTAL` line +
failure-reason breakdown at the end) and reports section 3 for a human to
read, since there's no single correct answer to grade there.

**Last real run against `http://192.168.1.145:11434`, model
`openrpg-npc_ep4a-llama3.2-3b:latest`, 8 trials/scenario** (worth
re-running rather than trusting these numbers indefinitely — see
`prompts/`'s own note on the same thing):
- Item identification: **32/32 valid, 32/32 right tool, 32/32 a real listed id** — across trees/fish/berries/sticks with 3-6 same-kind options in sight, the recovery fix in `BuildAction` plus the model's own targeting held up perfectly.
- `REAL LOG: "lets go fishing guys"`: **6/8** correctly called `catch_fish`; the other 2 called `follow -> Alex` instead — the same failure shape the flagged play session showed, now with a concrete baseline rate rather than an anecdote.
- `interrupt (mid pick_apple walk, player calls out directly)`: **0/8** kept gathering, but appropriately so — every trial responded to the player instead (mostly `speak`), which is the actually-correct behavior for a direct address.
- Sleep-reaffirmation: tried and dropped. A `CURRENTLY: mid-sleep` scenario measured **0/36** reaffirmations across two personalities, two harnesses, and two attempted prompt fixes (`ActInstruction`, then `ThinkInstruction` too) — never once landed. Root-caused as the wrong kind of fix entirely: sleep doesn't need better prompt reasoning about whether to continue, it needs to never be asked at all (see `NpcAgent.ShouldReopenDecision`'s own header — sleep is now excluded from this whole reopen mechanism, woken only by an actual attack, same as before this session started). The scenario itself was removed from this suite since `CURRENTLY` can no longer ever show "sleep" in the real game.
- `interrupt (mid-travel, a wolf just came into view)`: results here are **not fully representative** — in real gameplay, `NpcAgent.DetectAlertAnimal`/`HandleAlert` would intercept a newly-sighted animal with a mechanical, non-LLM reflex before a full `Decide()` call ever happens; this scenario calls `Decide()` directly, bypassing that reflex layer, so it measures something adjacent to real behavior, not real behavior itself. Kept in the suite as a still-informative "if the model alone had to weigh this" data point, not a graded pass/fail.
- One general-scenario failure: `invalid_target_river` (a `travel` call hallucinating a landmark that was never offered) — expected and unfixed on purpose, since `travel` was deliberately excluded from the target-recovery fix (see that fix's own comment in `Mind.cs` for why: a landmark's name carries meaning a resource id doesn't).

## `prompts/` — live-model smoke tests, needs a running Ollama

`run_prompt_tests.py` sends real requests to a local Ollama instance
(reusing `llm_tuning/common.py`'s persona/situation/tool builders, same
fidelity rule that file already follows) for three scenarios exercising
this session's prompt changes: `listen` offered on overheard (non-direct)
speech, a `RECENT ACTIONS` repetition streak, and `wait` framed as a last
resort with nothing else obviously to do. Not a graded benchmark like
`llm_tuning/5_baseline_eval.py` — there's no single correct answer for
most of these (personality- and temperature-dependent), so it just runs
each scenario a few times and prints what the model actually chose, for
a human to read. `ValidActionStressTest.cs` above now covers the same
ground (plus item identification and real-log scenarios) through the
real C# code path rather than a Python re-implementation — prefer that
one for anything that needs production fidelity; keep this one for quick
one-off exploration without a Godot binary handy.

Run (needs Ollama running locally with the model already pulled — see
`llm_tuning/6_export_to_ollama.py`):
```
python3 tests/prompts/run_prompt_tests.py --base-url http://<your-ollama-ip>:11434 --trials 10
```

**Last real run** (`.145`, 10 trials/scenario): `listen` 10/10, RECENT
ACTIONS anti-repeat 10/10 switched off the streak, wait-as-last-resort
0/10 (never defaulted to it) — all landing well. This script used to
also carry a `CURRENTLY: mid-sleep` scenario (0/20 reaffirmations,
matching `ValidActionStressTest`'s own findings) — removed once that
turned out to need a mechanical fix, not a prompt one; see
`gameplay/`'s own note above for the full story.
