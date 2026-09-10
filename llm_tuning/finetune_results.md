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

**Caveat that matters more than any single number here**: `--val-only` is
25 examples. A back-to-back same-model, same-config comparison recorded
in `README.md` showed the *base* model alone swinging 80%→72% between two
consecutive unseeded runs — an 8-point swing from sampling noise, zero
code changes. Both `speak` 3/6 results above are a real, low-variance
signal worth trusting more than the overall percentage (base model's most
recently recorded `speak` recall was 2/6 on the same runs) — but "84.0%
beats the base model's ~78% mean" isn't confirmed at n=3 tuned runs vs
n=7 base runs. See `README.md`'s "Results so far" for the full
multi-run table and the still-open question of whether a fresh,
never-trained-on `not_available` batch would show real generalization or
just memorization of the 46 training examples.
