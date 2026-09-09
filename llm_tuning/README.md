# OpenRPG NPC tool-calling fine-tune

Why this exists, and why it's narrower than it first looked: the
2026-09-07 play session logs showed `llama3.2:3b` defaulting to `speak` on
well over half of all NPC decisions. Two real infra bugs got fixed the
next day — `OllamaProvider` was silently loading the model at Ollama's own
4096-token default regardless of what the model actually supports
(`MindConfig.NumCtx`), and `analytics.local`'s mDNS hostname was costing
~5.1s of pure DNS dead time on every single request (`MindConfig.BaseUrl`)
— and a from-scratch baseline eval (`5_baseline_eval.py`, live against
Ollama, not simulated) showed the model was never nearly as broken as
those logs implied: **~79-83% zero-shot accuracy** across a full labeled
set, once those bugs were gone. Prompt-level fixes (a "can't vs won't"
instruction clause, vision-radius tool-menu gating, a full clean/
labeled-section prompt rewrite) pushed several specific confusions
(`pick_apple`/`gather_pinecone`, `follow`) to 75-100%.

**What survived every prompt-level fix tried, and is the actual reason
this fine-tune is still worth running**: reliably declining when the
specific thing asked for genuinely isn't available right now (no apple
tree in sight, fire not lit, nothing to trade). Benchmarked across 11
resource/action categories (`llm_tuning/data`'s `not_available`-tagged
examples), the model gets this right for `sleep` (a fact it can read
straight off a vitals number) and fails almost everywhere else — it
reaches for an unrelated available tool (`follow`, `gather_berry`) instead
of admitting it can't, or goes silent. That's the actual target behavior
for stage 4, not "the model hedges on everything."

Same overall approach as
[llm_financial_news_market_data](https://github.com/alexanderdolotov/llm_financial_news_market_data):
Unsloth LoRA fine-tune of a small model, exported to GGUF, served from a
local Ollama instance. Numbered pipeline scripts, in order — **run
`5_baseline_eval.py` before stage 4 every time this dataset or prompt
changes**; it's cheap (~90s, no GPU needed) and repeatedly caught this
project chasing problems that infra or prompt fixes alone already solved.

## The core pattern being taught

| player says | this is a... | correct response |
|---|---|---|
| "how many sticks do you have?" | QUESTION | `speak`, with the real inventory count |
| "let's go collect sticks" | REQUEST | `pick_up_stick` tool call, called this turn |
| "can you make me a torch?" (fire unlit) | REQUEST, unavailable | `speak`, an honest decline — **the weak spot** |
| "how many apples do you have? let's get pinecones" | QUESTION + REQUEST | the REQUEST wins (time-sensitive; the fact isn't) |

## Pipeline

- **`common.py`** — shared constants and helpers, ported *character-for-
  character* from the real game (`Mind.PlayerRequestInstruction`/
  `ActInstruction`, `Mind.BuildTools()`, `Personality.DescribeForPrompt()`,
  `NpcAgent.BuildPerception()`). The clean, labeled-section prompt rewrite
  (2026-09-08) — validated via `5_baseline_eval.py` against the old
  dense-prose prompt with no real regressions once lenient-parse scoring
  was fixed — **is now what the real game sends too**, ported into
  `Mind.cs`/`NpcAgent.cs`/`Personality.cs` the same day. Structural notes
  from that port worth knowing: (1) STATS moved into the system message
  (`Mind.Decide`/`DecidePlayerRequest` now take a `statsLine` parameter,
  appended after `Personality.DescribeForPrompt()`) rather than living in
  `Personality` itself, which still doesn't know about `CharacterStats`;
  (2) resource lines (trees/fish/pine/berry) sit under ENVIRONMENT, always
  shown, NOT sharing NEARBY's budgeted guaranteed-diversity-then-nearest
  pool with people/animals/sticks — an EARLIER version of this port tried
  merging them into that shared pool (matching `BuildPerception()`'s own
  pre-existing computation more literally) and a live A/B run measured
  `pick_apple` recall crash from a consistent 66-83% to 8.3% — reverted
  the same day, in both `common.py` and the real game, once that
  regression showed up. `base_targets()`'s `light_fire_allowed` also
  changed default (True, not False) — see its own comment for the real,
  dataset-wide bug that fixed. And `situation_for()` gained a
  `witnessed=` param (2026-09-08) — freshWitnessed ("You just saw: ...")
  shares NpcAgent's HEARD section with freshHeard but had no training
  coverage at all until a code-review pass caught the gap.

- **`1_generate_scenarios.py`** / **`2_claude_labeled_examples.py`**
  *(done)* — stage 1 is deterministic/templated (zero labeling risk,
  volume); stage 2 is hand-authored for real phrasing variety a template
  can't produce, plus the harder cases (compound question+request,
  third-party-overheard suggestions, a witnessed-event reaction). Current
  output: 226 + 30 = **256 examples**, spanning all 18 actions, both
  player-request and ambient shapes, and `not_available` decline coverage
  across 11 categories (was 4). Re-run after editing either:
  `python 1_generate_scenarios.py && python 2_claude_labeled_examples.py`.

- **`3_build_dataset.py`** *(done)* — merges + shuffles (seeded) + 90/10
  split -> `data/train.jsonl` (231) / `data/val.jsonl` (25). Warns if any
  action appears in val but not train.

- **`5_baseline_eval.py`** *(done, verified)* — zero-shot eval, live
  against Ollama, no training-stack dependencies (stdlib only). Replicates
  `OllamaProvider.ChatInternal`'s exact request shape, per-personality
  temperature (`Personality.Temperature`'s formula), and
  `Mind.ParseToolCall`'s `LenientParseFromText` fallback (added
  2026-09-08 — a real, observed case: the model chose the right tool and
  target but wrote it as malformed JSON in plain content instead of a
  structured call; scoring that as a flat miss significantly understated
  real accuracy). Reusable post-tune as-is: `--model <tuned_tag>`. Run
  `--val-only` for a quick, held-out-only check; omit it to eval the full
  train+val set (more categories, more stable per-category numbers, no
  leakage concern since nothing's been trained on it yet).

- **`4_finetune_tool_calling.py`** *(written, UNVERIFIED on this box —
  no GPU here)* — Unsloth LoRA fine-tune of `unsloth/Llama-3.2-3B-Instruct`
  (ungated mirror), 4-bit, `SFTConfig(completion_only_loss=True)`. Module
  docstring flags the likely first-run snags (chat-template name,
  `completion_only_loss` masking behavior by `trl` version). Meant to run
  on `analytics.local` (GPU + training stack already set up there from
  prior fine-tunes — `news-sentiment-*`/`phrasebank-llama-*` tags visible
  in `ollama list` are its own past output). Treat the first run there as
  a real debugging pass per the module's own docstring, not a formality.

- **`6_export_to_ollama.py`** *(written, UNVERIFIED)* — GGUF export
  (`save_pretrained_gguf`, `Q4_K_M` — confirmed to match the base model's
  own quantization via `ollama show llama3.2:3b`) + `ollama create`,
  registering the tuned model under `MODEL_MAP`'s `ollama_tuned_tag`
  (`openrpg-npc-llama3.2-3b`). Prints the exact `5_baseline_eval.py`
  command to run next — same script, same methodology, directly
  comparable to the base model's own numbers.

## Data format

`data/scenarios.jsonl`, one JSON object per line:

```json
{
  "mode": "player_request",
  "system": "<persona line>\n\n<PLAYER_REQUEST_INSTRUCTION or ACT_INSTRUCTION>",
  "user": "<situation text, same shape as BuildPerception()>",
  "tools": [ /* same shape as Mind.BuildTools() output */ ],
  "response": {"name": "pick_up_stick", "arguments": {"target_id": "stick_0", "emotion": "neutral"}},
  "tags": ["request", "pick_up_stick"]
}
```

## Running this on `analytics.local`

```
# from this Mac:
cd "/Users/alexdolotov/Library/CloudStorage/OneDrive-Personal/Projects/games/openrpg"
rsync -av --exclude='__pycache__' --exclude='outputs' --exclude='data/*.json' \
  llm_tuning/ root@analytics.local:/data/openrpg/llm_tuning/

# on analytics.local, in /data/openrpg/llm_tuning/:
python3 5_baseline_eval.py --val-only          # sanity check before spending GPU time
python3 4_finetune_tool_calling.py             # produces outputs/llama3.2-3b/final_adapter/
python3 6_export_to_ollama.py                  # GGUF export + ollama create — see its own note below
                                                # if ollama create doesn't pick up the change:
ollama rm openrpg-npc-llama3.2-3b
ollama create openrpg-npc-llama3.2-3b -f outputs/llama3.2-3b/gguf_gguf/Modelfile

# back on this Mac (or anywhere with network access to analytics.local):
python3 llm_tuning/5_baseline_eval.py --model openrpg-npc-llama3.2-3b:latest --val-only
```

`data/*.json` (the per-run raw eval results this session's `5_baseline_eval.py --out`
runs accumulated) are excluded from the sync deliberately — only
`train.jsonl`/`val.jsonl`/`scenarios.jsonl`/`claude_labeled.jsonl` matter
for training; the rest are this Mac's own debugging history.

To actually use the tuned model in the game: `mind.local.json`'s `"model"`
field points at whatever Ollama tag you want — set it to
`"openrpg-npc-llama3.2-3b:latest"` once `6_export_to_ollama.py`/`ollama
create` has registered it (confirm with `curl analytics.local:11434/api/tags`
or `ollama list` on that box).

## Results so far (2026-09-08)

**First attempt (`learning_rate=2e-4`, 3 epochs): a real regression.**
`--val-only` dropped from ~80% (base) to 36.0% (9/25), collapsing toward
`speak` almost everywhere (`catch_fish`→speak, `cook_meat`→speak×3,
`light_fire`→speak×2) — the exact failure shape this whole project set out
to fix in the first place. Root cause: `2e-4` on a ~231-example dataset for
3 epochs is aggressive enough to overcook a 3B LoRA into collapsing toward
whatever action dominates the training mix, rather than learning the
narrow correction it was meant to. Worth flagging for next time: only 46 of
231 training examples (20%) are the actual target behavior
(`not_available` declines) — the other 80% reinforces things the base
model already gets right, diluting the gradient signal for the one thing
that needed to change. A future attempt might do better weighting the
dataset more heavily toward the target behavior, not just lowering the
learning rate.

**Second attempt (`learning_rate=2e-5`): no collapse, a small possible
gain, not yet statistically confirmed.** Single runs looked promising
(84.0%, beating base's 80.0% on the same val set) — but a direct
back-to-back comparison exposed how little a single 25-example run
proves: the *base* model alone, unseeded, swung from 80% to 72% between
two consecutive runs with zero changes. Collecting more runs of each
(`--val-only`, unseeded, `analytics.local` otherwise idle):

| | n | mean | range |
|---|---|---|---|
| base (`llama3.2:3b`) | 7 | 78.3% | 72-84% |
| tuned (`openrpg-npc-llama3.2-3b`) | 3 | 81.3% | 80-84% |

The tuned model's distribution sits at or above the *top* of the base
model's range and never dropped as low as the base model's own 72-76%
low end across any of its 3 runs — a genuinely encouraging pattern, not
nothing. But n=3 vs n=7 on a 25-example val set isn't enough to call this
settled either way. One specific, lower-variance signal worth watching:
`speak` recall (where the `not_available` declines live) came back 3/6
(50%) in all 3 tuned runs, vs 2/6 (33.3%) in the base model's two most
recently recorded runs — consistent enough across repeats to be more
interesting than the noisy overall number, though still thin evidence on
its own.

**Open question, raised directly and not yet resolved**: is fine-tuning
even the right lever here, or would a cheaper fix (few-shot examples
embedded directly in the prompt, or a structural change like an explicit
`cant_do_that(reason)` tool sidestepping the negative-inference problem
entirely) get most of the same benefit without training risk? Worth
trying before sinking more GPU time into further fine-tuning rounds.
**Next step to actually settle the "did it generalize or memorize"
question**: build a fresh batch of `not_available` examples the model
never trained on (new resource combos, new phrasings) and eval both
models against just that — the real test of generalization vs.
memorizing the 46 training examples.



