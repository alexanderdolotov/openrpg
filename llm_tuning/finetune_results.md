# Fine-tune eval results

## Baseline — untuned `llama3.2:3b`, full 255-example set (worst case)

Not a `--val-only` run like the others below — the full train+val set, run
on this Mac before the `pick_apple`/`ENVIRONMENT` regression fix landed
(see `README.md`'s own "Results so far"), so this is close to the worst
number the base model has posted this project. Included here specifically
as the low end of the comparison range, not as "the" baseline — the
`README.md` multi-run table (base mean 78.3%, n=7, range 72-84% on
`--val-only`) is the more representative one.

```
=== Overall: 188/255 correct (73.7%) ===

--- By mode ---
  ambient          28/32 (87.5%)
  player_request   160/223 (71.7%)

--- Per-expected-action recall ---
  attack               4/5 (80.0%)
  catch_fish           20/23 (87.0%)
  cook_meat            4/5 (80.0%)
  deposit              4/4 (100.0%)
  eat                  4/4 (100.0%)
  follow               16/16 (100.0%)
  gather_berry         9/9 (100.0%)
  gather_pinecone      9/13 (69.2%)
  light_fire           20/23 (87.0%)
  make_torch           17/17 (100.0%)
  pick_apple           10/12 (83.3%)
  pick_up_stick        21/25 (84.0%)
  sleep                4/4 (100.0%)
  speak                42/89 (47.2%)
  trade                4/5 (80.0%)
  wait                 0/1 (0.0%)

--- target_id match rate (given the right action) ---
  86/97 (88.7%)
```

Source: `data/expanded_dataset_2026-09-08.json` (this Mac, gitignored —
regenerate with `python 5_baseline_eval.py` from that commit if needed).

## Tuned — `openrpg-npc_ep4a-llama3.2-3b`, `--val-only`

```
((pyml) ) root@localhost:/data/openrpg/llm_tuning#  python 5_baseline_eval.py --model openrpg-npc_ep4a-llama3.2-3b:latest --val-only
Evaluating 25 examples from ['/data/openrpg/llm_tuning/data/val.jsonl']
model=openrpg-npc_ep4a-llama3.2-3b:latest base_url=http://192.168.1.145:11434 num_ctx=4096 concurrency=4
  10/25...
  20/25...
  25/25...
Done in 12.0s (0.48s/example avg, concurrency=4)

=== Overall: 21/25 correct (84.0%) ===

--- Confusion matrix (rows = expected action, cols = predicted) ---
                       catch_fish       cook_meat             eat    gather_berry gather_pinecone      light_fire      make_torch      pick_apple   pick_up_stick           speak           trade          travel
catch_fish                      2               0               0               0               0               0               0               0               0               0               0               0
cook_meat                       0               2               0               0               0               0               1               0               0               0               0               0
eat                             0               0               1               0               0               0               0               0               0               0               0               0
gather_berry                    0               0               0               3               0               0               0               0               0               0               0               0
gather_pinecone                 0               0               0               0               3               0               0               0               0               0               0               0
light_fire                      0               0               0               0               0               3               0               0               0               0               0               0
pick_apple                      0               0               0               0               0               0               0               1               0               0               0               0
pick_up_stick                   0               0               0               0               0               0               0               0               3               0               0               0
speak                           0               0               0               0               0               0               0               0               0               3               1               2

--- By mode ---
  ambient          2/2 (100.0%)
  player_request   19/23 (82.6%)

--- Per-expected-action recall ---
  catch_fish           2/2 (100.0%)
  cook_meat            2/3 (66.7%)
  eat                  1/1 (100.0%)
  gather_berry         3/3 (100.0%)
  gather_pinecone      3/3 (100.0%)
  light_fire           3/3 (100.0%)
  pick_apple           1/1 (100.0%)
  pick_up_stick        3/3 (100.0%)
  speak                3/6 (50.0%)

--- target_id match rate (given the right action) ---
  12/12 (100.0%)
```

## Tuned — `openrpg-npc_ep5a-llama3.2-3b`, `--val-only`

```
((pyml) ) root@localhost:/data/openrpg/llm_tuning#  python 5_baseline_eval.py --model openrpg-npc_ep5a-llama3.2-3b:latest --val-only
Evaluating 25 examples from ['/data/openrpg/llm_tuning/data/val.jsonl']
model=openrpg-npc_ep5a-llama3.2-3b:latest base_url=http://192.168.1.145:11434 num_ctx=4096 concurrency=4
  10/25...
  20/25...
  25/25...
Done in 12.4s (0.50s/example avg, concurrency=4)

=== Overall: 21/25 correct (84.0%) ===

--- Confusion matrix (rows = expected action, cols = predicted) ---
                       catch_fish       cook_meat             eat    gather_berry gather_pinecone      light_fire      make_torch      pick_apple   pick_up_stick           speak           trade          travel
catch_fish                      2               0               0               0               0               0               0               0               0               0               0               0
cook_meat                       0               2               0               0               0               0               1               0               0               0               0               0
eat                             0               0               1               0               0               0               0               0               0               0               0               0
gather_berry                    0               0               0               3               0               0               0               0               0               0               0               0
gather_pinecone                 0               0               0               0               3               0               0               0               0               0               0               0
light_fire                      0               0               0               0               0               3               0               0               0               0               0               0
pick_apple                      0               0               0               0               0               0               0               1               0               0               0               0
pick_up_stick                   0               0               0               0               0               0               0               0               3               0               0               0
speak                           0               0               0               0               0               0               0               0               0               3               1               2

--- By mode ---
  ambient          2/2 (100.0%)
  player_request   19/23 (82.6%)

--- Per-expected-action recall ---
  catch_fish           2/2 (100.0%)
  cook_meat            2/3 (66.7%)
  eat                  1/1 (100.0%)
  gather_berry         3/3 (100.0%)
  gather_pinecone      3/3 (100.0%)
  light_fire           3/3 (100.0%)
  pick_apple           1/1 (100.0%)
  pick_up_stick        3/3 (100.0%)
  speak                3/6 (50.0%)

--- target_id match rate (given the right action) ---
  11/12 (91.7%)
```

## Head-to-head, full 256-example set — vanilla vs. tuned, same prompt (2026-09-09)

The first comparison run at real sample size (not `--val-only`'s 25), and
the first genuinely convincing evidence the fine-tune is doing real work.
Motivated by a live prompt_debug session (see `README.md`'s own log) that
caught the exact `not_available` substitution twice in real gameplay —
`PlayerRequestInstruction` got a new, concrete, named example the same
day (asked to pick apples/catch fish with the tool not listed, name the
substitution failure to avoid explicitly) — dataset regenerated to match,
then both models benchmarked against the identical 256-example set so the
prompt is the only thing NOT varying between the two runs.

```
=== vanilla llama3.2:3b — Overall: 193/256 correct (75.4%) ===
--- Per-expected-action recall ---
  attack               4/5 (80.0%)
  catch_fish           23/23 (100.0%)
  cook_meat            4/5 (80.0%)
  deposit              4/4 (100.0%)
  eat                  4/4 (100.0%)
  follow               16/16 (100.0%)
  gather_berry         10/10 (100.0%)
  gather_pinecone      9/13 (69.2%)
  light_fire           18/23 (78.3%)
  make_torch           17/17 (100.0%)
  pick_apple           10/12 (83.3%)
  pick_up_stick        21/25 (84.0%)
  sleep                4/4 (100.0%)
  speak                45/89 (50.6%)
  trade                4/5 (80.0%)
  wait                 0/1 (0.0%)
--- target_id match rate (given the right action) ---
  90/101 (89.1%)
Source: data/concrete_decline_example_2026-09-09.json

=== openrpg-npc_ep4a-llama3.2-3b — Overall: 205/256 correct (80.1%) ===
--- Per-expected-action recall ---
  attack               4/5 (80.0%)
  catch_fish           21/23 (91.3%)
  cook_meat            4/5 (80.0%)
  deposit              4/4 (100.0%)
  eat                  4/4 (100.0%)
  follow               16/16 (100.0%)
  gather_berry         10/10 (100.0%)
  gather_pinecone      9/13 (69.2%)
  light_fire           22/23 (95.7%)
  make_torch           17/17 (100.0%)
  pick_apple           10/12 (83.3%)
  pick_up_stick        21/25 (84.0%)
  sleep                4/4 (100.0%)
  speak                55/89 (61.8%)
  trade                3/5 (60.0%)
  wait                 1/1 (100.0%)
--- target_id match rate (given the right action) ---
  98/98 (100.0%)
Source: data/concrete_decline_example_tuned_2026-09-09.json
```

`not_available`-tagged examples specifically (46 total, the actual target
behavior — see `README.md`'s "Why this exists"):

| category | vanilla | tuned |
|---|---|---|
| sleep | 4/4 | 4/4 |
| attack | 3/5 | 5/5 |
| gather_berry | 0/4 | **4/4** |
| gather_pinecone | 1/4 | 3/4 |
| pick_apple | 0/4 | 1/4 |
| catch_fish | 1/4 | 0/4 |
| cook_meat / light_fire / make_torch / pick_up_stick / trade | 0/4 or 0/5 | 0/4 or 1/5 |
| **total** | **9/46 (19.6%)** | **18/46 (39.1%)** |

Tuned model roughly **doubles** the vanilla model's `not_available`
correctness, and `speak` recall overall (61.8% vs 50.6%) tells the same
story. Mixed, not clean, on the two categories the new instruction
actually *names*: `pick_apple` improves slightly (0/4→1/4) but
`catch_fish` goes the other way (1/4→0/4) — the instruction's gain isn't
concentrated where it was aimed, more evidence the two techniques
(prompting, fine-tuning) are doing different, only partly overlapping
work rather than the fine-tune just "knowing the instruction already."

Caveat still worth stating plainly: this evaluates against the same 46
underlying scenarios `ep4a` trained on (with new instruction wording it
never saw during training, since that got added after this adapter was
produced) — real signal, but not the fully clean
never-seen-this-scenario-at-all generalization test described below.

Side note, not about accuracy: both runs ran unusually slow (~1.2-1.3s/
example vs. the usual ~0.35-0.5s) — `base_url` defaulted to the
`analytics.local` hostname this time instead of a bare IP, right in line
with `MindConfig.BaseUrl`'s own documented ~5.1s/request DNS tax. Doesn't
affect either run's accuracy (same cost hit both equally), just their
wall-clock time.

# Discussion

Best tool-action fine-tuning results so far: training llama3.2:3b for 4-5
epochs at `learning_rate=2e-5`. Both runs land at 84.0% (21/25) on
`--val-only`, `speak` recall 3/6 (50.0%) in both — consistent with each
other, and with the earlier `openrpg-npc-llama3.2-3b:latest` (the actual
deployed tag) run reported in `README.md`.

At the higher `learning_rate=2e-4`, after 3 epochs validation collapsed —
recorded as low as 20% in one run, 36.0% (9/25) in another, both
collapsing toward `speak` regardless of the right answer — the same
failure shape this whole fine-tune was meant to fix in the first place.
Small nudges (lower LR, more epochs) tune the existing model toward
better tool activation; aggressive ones overcook a 3B LoRA on this small
a dataset into collapse.

**Caveat that mattered at the time, largely resolved since**: `--val-only`
is only 25 examples, and a back-to-back same-model, same-config comparison
recorded in `README.md` showed the *base* model alone swinging 80%→72%
between two consecutive unseeded runs — an 8-point swing from sampling
noise, zero code changes — which was enough to make "84.0% beats the base
model's ~78% mean" genuinely unconfirmed at the time (n=3 tuned runs vs
n=7 base runs).

The head-to-head section above is the actual resolution: same 256-example
set, same prompt, both models, run back to back — tuned wins 80.1% vs
75.4% overall, and **doubles** vanilla's `not_available` correctness
(39.1% vs 19.6%). That gap is large enough, at that sample size, to not
plausibly be the same run-to-run noise that made the `--val-only`
comparisons ambiguous. The fine-tune is doing real work.

What's still open: this remains an eval against the *same 46 scenarios*
`ep4a` trained on, just with instruction wording added after training —
real signal, not proof of generalization to genuinely novel
`not_available` situations (new resource combos, new phrasings never seen
in training at all). That fresh, never-trained-on batch is still the
clean test, if there's ever a reason to doubt whether this holds up
outside the training distribution. And within the distribution it does
know, there's still real room: `cook_meat`/`light_fire`/`make_torch`/
`pick_up_stick`/`trade` sit at 0/4 or 1/5 for *both* models — not a
fine-tuning-vs-prompting question at all, just still-unsolved.

# Backstory-grounding + collaborative-default instruction (2026-09-10)

Motivated by a real gameplay log: Finn declined an apple-picking request
with "Not my thing" despite his own `BACKGROUND` text stating he's
"never turned down a good apple either" — a straight contradiction
between the decline reason and a fact the prompt itself hands the model.
Not the same failure as `not_available` tool-substitution (the
`pick_apple` tool *was* offered here); this is a grounding/consistency
gap.

Fix, applied to `PlayerRequestInstruction` in `Mind.cs` (mirrored
character-for-character into `common.PLAYER_REQUEST_INSTRUCTION`, see
`llm_tuning/common.py`): two new sentences telling the model (a) to check
any reason it gives, for going along with a request or declining it,
against its own `BACKGROUND`/`PERSONALITY` and never invent one that
contradicts a stated fact, and (b) to default toward helping when it
reasonably can — framed explicitly as "a real reason to say no is fine,
declining with no real reason isn't personality, it's just unhelpful."
Deliberately did **not** touch `personality_archetypes.json` trait
values — the goal was more honest, better-grounded declines, not
flattening the intentional variety between e.g. `gruff_loner` (Bram) and
`warm_socializer`/`easygoing_wanderer` (Maren/Finn).

Re-ran the full 256-example set (`5_baseline_eval.py`, `--seed 42`)
against `openrpg-npc_ep4a-llama3.2-3b:latest` with the new instruction in
place, same dataset regeneration + character-for-character verification
+ `dotnet build` (0 errors) workflow as every other prompt change this
session:

| | overall | `not_available` |
|---|---|---|
| ep4a, old instruction (see head-to-head above) | 80.1% | 39.1% (18/46) |
| ep4a, new instruction | 82.8% (212/256) | 50.0% (23/46) |

Both figures move the right direction — overall +2.7pt, and
`not_available` specifically +10.9pt, on top of a category that was
already the weakest across every run this session. Remaining
`not_available` misses cluster in one recognizable shape: the model
substitutes a *plausible neighboring action* instead of declining —
`light_fire`↔`make_torch` (4 misses), `cook_meat`→`make_torch` (3
misses), `pick_up_stick`→`gather_pinecone` (2 misses) — action pairs
that are conceptually adjacent (fire-related, gathering-related), not
random noise. That looks like a distinct, narrower problem from the
original "just travel/gather_berry as a catch-all" pattern this session
started from, and a plausible next target if `not_available` gets
revisited again.

Caveat carried forward unchanged from the section above: this is still
an eval against the same 46 `not_available` scenarios `ep4a` trained on,
not a fresh generalization test. Have not yet re-run the vanilla
(untuned) model against this new instruction — the head-to-head above
used the previous wording, so a like-for-like vanilla comparison on the
new instruction is still open if that comparison is ever needed again.
