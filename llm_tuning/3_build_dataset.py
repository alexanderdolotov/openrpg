#!/usr/bin/env python3
"""Stage 3 — merge stage 1 + stage 2 output into one shuffled train/val
split. Stays at the plain system/user/tools/response level on purpose —
turning that into a specific base model's real chat-template tokens
(Llama 3.2's tool-calling format) is a tokenizer-specific step that
belongs in stage 4 where the tokenizer actually is, not baked into a
static file here (same separation the finance repo's to_chat_dataset()
draws, just applied at train time instead of at this stage).

Run: python 3_build_dataset.py
Output: data/train.jsonl, data/val.jsonl
"""
from __future__ import annotations

import json
import random
from pathlib import Path

DATA_DIR = Path(__file__).parent / "data"
SOURCES = ["scenarios.jsonl", "claude_labeled.jsonl"]
VAL_FRACTION = 0.1
SEED = 20260907


def load(path: Path) -> list[dict]:
    if not path.exists():
        print(f"  (skipping {path.name} — not found; run its generator stage first)")
        return []
    with path.open() as f:
        return [json.loads(line) for line in f if line.strip()]


def main():
    examples: list[dict] = []
    for name in SOURCES:
        rows = load(DATA_DIR / name)
        print(f"loaded {len(rows)} from {name}")
        examples.extend(rows)

    if not examples:
        raise SystemExit("no examples found — run 1_generate_scenarios.py (and optionally 2_claude_labeled_examples.py) first")

    rng = random.Random(SEED)
    rng.shuffle(examples)

    n_val = max(1, int(len(examples) * VAL_FRACTION))
    val, train = examples[:n_val], examples[n_val:]

    with (DATA_DIR / "train.jsonl").open("w") as f:
        for ex in train:
            f.write(json.dumps(ex) + "\n")
    with (DATA_DIR / "val.jsonl").open("w") as f:
        for ex in val:
            f.write(json.dumps(ex) + "\n")

    print(f"\n{len(train)} train / {len(val)} val -> data/train.jsonl, data/val.jsonl")

    # Sanity check: every action that appears in val should also appear in
    # train, or that action's val accuracy is meaningless (zero training
    # signal for it). Small dataset + random split means this is worth
    # actually checking, not assuming.
    train_actions = {ex["response"]["name"] for ex in train}
    val_only = {ex["response"]["name"] for ex in val} - train_actions
    if val_only:
        print(f"WARNING: action(s) {val_only} appear in val but not train — re-run with a different split "
              f"(or more examples for that action) before trusting val accuracy on it.")


if __name__ == "__main__":
    main()
