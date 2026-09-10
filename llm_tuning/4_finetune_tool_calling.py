#!/usr/bin/env python3
"""Stage 4 — LoRA fine-tune of Llama-3.2-3B-Instruct on the tool-calling
dataset from stage 3, via Unsloth. Same core recipe as
5_finetune_news_sentiment.py / 6_finetune_news_gold_f1.py in
https://github.com/alexanderdolotov/llm_financial_news_market_data:
Unsloth 4-bit load, LoRA adapter, SFTConfig(completion_only_loss=True) so
gradient signal concentrates on the tool-call completion rather than the
(long — persona + instructions + full tool schema) prompt.

UNVERIFIED as written — this dev environment has no GPU and no
transformers/unsloth installed (checked; both absent), so this is written
to the documented Unsloth conversational/tool-calling recipe and your own
finance repo's proven call shape, not run end-to-end. Treat the first run
on your box as a real debugging pass, not a formality — likely candidates
if something breaks: (a) `meta-llama/Llama-3.2-3B-Instruct` is a gated HF
repo, needs `huggingface-cli login` with an accepted-license token, or use
`unsloth/Llama-3.2-3B-Instruct` (Unsloth's own mirror, usually ungated);
(b) exactly how `completion_only_loss` masks a `messages`+`tools`
conversational dataset can shift between trl versions — if it doesn't mask
automatically, fall back to an explicit `response_template` matched
against the rendered text (see the commented alternative below); (c) chat
template name — try "llama-3.1" first (same family Llama 3.2 uses in
Unsloth's template list as of this writing), fall back to "llama-3" or
check `unsloth.chat_templates.CHAT_TEMPLATES.keys()` if the tool-call
turn doesn't round-trip the way you expect.

Run: python 4_finetune_tool_calling.py
Output: outputs/<run_name>/ (LoRA adapter + checkpoints)
"""
from __future__ import annotations

import json
from pathlib import Path

from common import MODEL_MAP

MODEL_KEY = "llama3.2:3b"

# Measured directly against the actual stage-1/2 output (see the .venv-less
# check that caught this): system+user+tools+response runs up to ~13.7K
# characters (~3,900 tokens at ~3.5 chars/token) per example, with a MEAN
# around 3,100 tokens — the full ActInstruction/PLAYER_REQUEST_INSTRUCTION
# text alone is ~3KB, and an 11-tool schema with real descriptions is
# another ~6KB. 2048 would silently truncate most examples, and since the
# tool-call response sits at the END of the rendered sequence, truncation
# would cut off the very label being trained on — a normal-looking loss
# curve over corrupted data. 4096 leaves real headroom above the observed
# max once chat-template special tokens are added on top.
MAX_SEQ_LENGTH = 4096
DATA_DIR = Path(__file__).parent / "data"
OUTPUT_DIR = Path(__file__).parent / "outputs" / MODEL_KEY.replace(":", "-")


def load_jsonl(path: Path) -> list[dict]:
    with path.open() as f:
        return [json.loads(line) for line in f if line.strip()]


def to_conversation(example: dict) -> dict:
    """One stage-3 example -> the {"messages": [...], "tools": [...]} shape
    HF's apply_chat_template (and Unsloth's wrapper around it) expects for
    tool-calling conversations. The assistant turn carries the ideal call
    as a structured tool_calls entry, not free text — this is what teaches
    the model to emit a real call instead of describing one.
    """
    return {
        "messages": [
            {"role": "system", "content": example["system"]},
            {"role": "user", "content": example["user"]},
            {
                "role": "assistant",
                "tool_calls": [{
                    "type": "function",
                    "function": {"name": example["response"]["name"], "arguments": example["response"]["arguments"]},
                }],
            },
        ],
        "tools": example["tools"],
    }


def main():
    from datasets import Dataset
    from unsloth import FastLanguageModel
    from unsloth.chat_templates import get_chat_template
    from trl import SFTConfig, SFTTrainer

    model_cfg = MODEL_MAP[MODEL_KEY]
    print(f"loading {model_cfg['hf_checkpoint']} (4-bit)...")
    model, tokenizer = FastLanguageModel.from_pretrained(
        model_name=model_cfg["hf_checkpoint"],
        max_seq_length=MAX_SEQ_LENGTH,
        load_in_4bit=True,
        dtype=None,  # let Unsloth pick (bfloat16 if supported)
    )

    # See module docstring (c) — confirm this is the right template name
    # for your installed unsloth version before trusting the rendered
    # output; a mismatched template silently produces a plausible-looking
    # but wrong prompt rather than an error.
    tokenizer = get_chat_template(tokenizer, chat_template="llama-3.1")

    model = FastLanguageModel.get_peft_model(
        model,
        r=16,
        lora_alpha=16,
        lora_dropout=0,
        target_modules=["q_proj", "k_proj", "v_proj", "o_proj", "gate_proj", "up_proj", "down_proj"],
        bias="none",
        use_gradient_checkpointing="unsloth",
        random_state=20260907,
    )

    train_rows = [to_conversation(ex) for ex in load_jsonl(DATA_DIR / "train.jsonl")]
    val_rows = [to_conversation(ex) for ex in load_jsonl(DATA_DIR / "val.jsonl")]
    print(f"{len(train_rows)} train / {len(val_rows)} val conversations")

    def render(batch):
        # tokenize=False here — SFTTrainer tokenizes; this just renders
        # each conversation through the chat template into one training
        # string, tool schema and all, ending with the assistant's call.
        return {"text": [
            tokenizer.apply_chat_template(m, tools=t, tokenize=False, add_generation_prompt=False)
            for m, t in zip(batch["messages"], batch["tools"])
        ]}

    train_ds = Dataset.from_list(train_rows).map(render, batched=True, remove_columns=["messages", "tools"])
    val_ds = Dataset.from_list(val_rows).map(render, batched=True, remove_columns=["messages", "tools"])

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    args = SFTConfig(
        output_dir=str(OUTPUT_DIR),
        per_device_train_batch_size=2,   # 8GB card + 3B/4bit at 4096 seq_length — drop to 1 first if this OOMs, raise if there's headroom
        gradient_accumulation_steps=8,   # effective batch 16
        num_train_epochs=5,              # small, narrow-behavior dataset — watch val loss, cut short if it turns up
        per_device_eval_batch_size=1,    # <--- ADD THIS
        eval_accumulation_steps=1,
        learning_rate=2e-5,
        logging_steps=5,
        eval_strategy="steps",
        eval_steps=20,
        save_steps=50,
        save_total_limit=2,
        completion_only_loss=True,       # mask loss to the assistant/tool-call tokens — same as the finance repo
        max_seq_length=MAX_SEQ_LENGTH,
        dataset_text_field="text",
        packing=False,                   # keep examples un-packed — clean per-example completion boundaries matter more than throughput here
        seed=20260907,
    )

    trainer = SFTTrainer(
        model=model,
        tokenizer=tokenizer,
        train_dataset=train_ds,
        eval_dataset=val_ds,
        args=args,
    )

    trainer.train()

    adapter_dir = OUTPUT_DIR / "final_adapter"
    model.save_pretrained(str(adapter_dir))
    tokenizer.save_pretrained(str(adapter_dir))
    print(f"\nsaved LoRA adapter -> {adapter_dir}")
    print("next: 6_export_to_ollama.py (GGUF export + ollama create), then re-run 5_baseline_eval.py")
    print(f"      --model {MODEL_MAP[MODEL_KEY]['ollama_tuned_tag']} to compare against the base model")


if __name__ == "__main__":
    main()
