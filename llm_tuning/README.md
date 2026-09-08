# OpenRPG NPC tool-calling fine-tune

Why this exists: the 2026-09-07 play session logs showed `llama3.2:3b`
(the model OpenRPG's NPCs run on, via `OllamaProvider.cs` ->
`http://analytics.local:11434`) defaulting to `speak` on well over half of
all NPC decisions — including direct player requests the prompt explicitly
tells it not to hedge on ("let's go fishing" got three politely-hedged
`speak` replies before anyone actually called `catch_fish`, ~3.5 minutes
later). `pick_up_stick` was called once and `make_torch` zero times across
two full play sessions, despite both being always fully available. That's
not a prompt bug — the mechanical tool-calling pipeline (`Mind.cs`) was
already hardened this same session — it's the small local model not
reliably following instructions. This is the fix: teach it the specific
behavior directly, via LoRA fine-tuning, rather than continuing to fight it
with prompt wording alone.

Same overall approach as
[llm_financial_news_market_data](https://github.com/alexanderdolotov/llm_financial_news_market_data):
Unsloth LoRA fine-tune of a small model, exported to GGUF, served from a
local Ollama instance. Numbered pipeline scripts, in order.

## The core pattern being taught

Straight from what the ask/logs called out:

| player says | this is a... | correct response |
|---|---|---|
| "how many sticks do you have?" | QUESTION | `speak`, with the real inventory count |
| "let's go collect sticks" | REQUEST | `pick_up_stick` tool call, called this turn |
| "how much health do you have?" | QUESTION | `speak`, with the real vitals fact |
| "can you help me make a fire?" | REQUEST | `light_fire` tool call |

Getting this distinction right — and, just as important, not overcorrecting
into calling a tool on every single turn regardless of what's actually
being asked — is the whole point of stage 1's dataset design: every
question example is paired with the same shape of request example, plus a
smaller set of honest-decline examples (personality-driven "no", and
"literally can't right now because X isn't available") so the model doesn't
learn "always act" as a shortcut.

## Pipeline

- **`common.py`** — shared constants and helpers. Everything here is
  ported *character-for-character* from the real game code
  (`Mind.PlayerRequestInstruction`/`ActInstruction`, `Mind.BuildTools()`,
  `NpcAgent.BuildPerception()`'s line shape). This fidelity is the whole
  ballgame: if a training example's system/user text doesn't match what
  `OllamaProvider.cs` actually sends over `/api/chat`, nothing learned here
  transfers back to real gameplay. **When `Mind.cs`'s instructions or tool
  schema change, port the change here in the same commit.**

- **`1_generate_scenarios.py`** *(done)* — generates a deterministic seed
  dataset with no API calls and no model needed: every label is read
  straight off the same state dict that built the situation text, so
  there's no labeling error at this stage. Current output: 186 examples in
  `data/scenarios.jsonl`, covering all 18 actions (deliberately
  over-representing `pick_up_stick`/`catch_fish`/`light_fire`/`make_torch`
  relative to how rarely they occur naturally), both the player-request
  and ambient (`Decide()`, no direct request) decision shapes, and the
  question/request/decline three-way split above. Re-run any time after
  editing it: `python 1_generate_scenarios.py`.

- **`2_claude_labeled_examples.py`** *(done)* — hand-authored, not
  templated: real phrasing variety a cartesian-product generator can't
  produce ("hey can u grab that stick real quick" / "fire's not gonna
  light itself"), harder question/decline boundary cases, and — the
  highest-value pattern here — the exact third-party-overheard failure
  from the real logs: an NPC reacting to *another NPC's* suggestion
  ("I think we should get some fish"), correctly labeled as actually
  calling the tool rather than just agreeing out loud again, paired with a
  genuine brush-off example so the model doesn't learn "always act on
  anything overheard" as a shortcut. 23 examples in
  `data/claude_labeled.jsonl`. This is the "a few good examples" bet you
  called out — small on purpose, stage 1 already covers volume. Extend by
  adding more calls to the same `pr()`/`amb()` helpers as new failure
  patterns turn up in future logs.

- **`3_build_dataset.py`** *(done)* — merges stage 1 + stage 2, shuffles
  (seeded), 90/10 train/val split -> `data/train.jsonl` / `data/val.jsonl`
  (209 total examples currently). Checks that every action appearing in
  val also appears in train (a real risk at this dataset size) and warns
  if not. Stays at the plain `system`/`user`/`tools`/`response` level on
  purpose — rendering into a specific base model's actual chat-template
  tokens is tokenizer-specific and belongs in stage 4, not baked into a
  static file here.

- **`4_finetune_tool_calling.py`** *(written, UNVERIFIED)* — Unsloth LoRA
  fine-tune of `unsloth/Llama-3.2-3B-Instruct`, 4-bit, `SFTConfig(completion_only_loss=True)`
  same as your finance repo (mask loss to the tool-call completion, not
  the long persona+instructions+schema prompt). **This dev box has no GPU
  and no transformers/unsloth installed** (checked directly), so unlike
  stages 1-3 — which I ran and inspected the output of — this is written
  to the documented Unsloth recipe and your own proven call shape, not
  actually run. The module docstring flags the three most likely first-run
  snags (gated HF checkpoint needing a token, `completion_only_loss`'s
  exact masking behavior on a `messages`+`tools` dataset varying by trl
  version, chat-template name). Treat the first run on your 8GB box as a
  real debugging pass.

- **`5_export_and_eval.py`** *(not built yet)* — GGUF export
  (`save_pretrained_gguf`), `Modelfile` + `ollama create`, then a held-out
  eval pass: same zero-shot-vs-fine-tuned comparison pattern as the
  finance repo's own training scripts, but scored on exact tool-name match
  (+ reasonable target_id) rather than AUC. Should specifically re-run the
  exact failing situations pulled from the real logs (the `pine_1`
  gather_pinecone / fishing / fire-lighting requests) as a regression
  check, not just aggregate accuracy. Worth building once stage 4 has
  actually run once on your box and produced a real adapter to point it
  at.

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

Deliberately backend-agnostic at this stage (plain dict, not a specific
chat template) — same separation the finance repo's `to_chat_dataset()`
draws between labeled data and training-time formatting.
