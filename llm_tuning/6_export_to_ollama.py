#!/usr/bin/env python3
"""Stage 6 — export stage 4's LoRA adapter to GGUF and register it with
Ollama, so it's servable exactly like the base model (same /api/chat shape
OllamaProvider.cs and 5_baseline_eval.py both already use). The missing
link the README originally called "5_export_and_eval.py" — split in two
since eval didn't need training-stack dependencies (unsloth/torch) at all
and already got built standalone as 5_baseline_eval.py, reusable as-is
against whatever Ollama tag this script produces.

UNVERIFIED as written — same posture as stage 4's own docstring: written
to Unsloth's documented save_pretrained_gguf recipe, not run end-to-end
(no GPU on the authoring machine). Likely first-run snags:
  (a) save_pretrained_gguf needs llama.cpp's quantization tooling
      available (Unsloth normally vendors/fetches it on first use) — if
      it errors looking for a converter, check Unsloth's own GGUF export
      docs for your installed version.
  (b) No custom Modelfile TEMPLATE here on purpose — Ollama reads the
      chat template embedded in the GGUF's own metadata by `save_
      pretrained_gguf`, which should already match whatever
      get_chat_template("llama-3.1") rendered during training. Hand-
      writing a second copy of that template here would risk it silently
      drifting from the one training actually used. If a real tool call
      doesn't round-trip correctly after import (check with the eval
      command this script prints at the end), that mismatch — not this
      script's Modelfile — is the first thing to check.
  (c) QUANT below matches llama3.2:3b's own default quantization on this
      box (confirmed via `ollama show llama3.2:3b`) — same footprint,
      not a heavier or lighter comparison than the base model it's
      benchmarked against.

Run: python 6_export_to_ollama.py
Output: outputs/<model>/gguf/*.gguf, outputs/<model>/Modelfile, and a new
Ollama model tag (MODEL_MAP[...]["ollama_tuned_tag"]) ready to eval.
"""
from __future__ import annotations

import subprocess
from pathlib import Path

from common import MODEL_MAP

MODEL_KEY = "llama3.2:3b"
OUTPUT_DIR = Path(__file__).parent / "outputs" / MODEL_KEY.replace(":", "-")
ADAPTER_DIR = OUTPUT_DIR / "final_adapter"
GGUF_DIR = OUTPUT_DIR / "gguf"
QUANT = "q4_k_m"


def main():
    from unsloth import FastLanguageModel

    model_cfg = MODEL_MAP[MODEL_KEY]

    if not ADAPTER_DIR.exists():
        raise SystemExit(f"no adapter found at {ADAPTER_DIR} — run 4_finetune_tool_calling.py first")

    print(f"loading base model + adapter from {ADAPTER_DIR}...")
    model, tokenizer = FastLanguageModel.from_pretrained(
        model_name=str(ADAPTER_DIR),
        max_seq_length=4096,
        load_in_4bit=True,
    )

    GGUF_DIR.mkdir(parents=True, exist_ok=True)
    print(f"exporting GGUF ({QUANT}) -> {GGUF_DIR} ...")
    model.save_pretrained_gguf(str(GGUF_DIR), tokenizer, quantization_method=QUANT)

    gguf_files = sorted(GGUF_DIR.glob("*.gguf"))
    if not gguf_files:
        raise SystemExit(f"no .gguf file found in {GGUF_DIR} after export — check the Unsloth output above")
    gguf_path = gguf_files[0]
    print(f"found {gguf_path}")

    modelfile_path = OUTPUT_DIR / "Modelfile"
    modelfile_path.write_text(f"FROM {gguf_path}\n")
    print(f"wrote {modelfile_path}")

    tag = model_cfg["ollama_tuned_tag"]
    print(f"running: ollama create {tag} -f {modelfile_path}")
    subprocess.run(["ollama", "create", tag, "-f", str(modelfile_path)], check=True)

    print(f"\ndone — eval it the same way as the base model, same script either way:")
    print(f"  python 5_baseline_eval.py --model {tag} --val-only")
    print(f"then compare directly against the base model's own run:")
    print(f"  python 5_baseline_eval.py --model {model_cfg['ollama_base_tag']} --val-only")


if __name__ == "__main__":
    main()
