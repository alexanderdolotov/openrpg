"""Zero-shot baseline eval: how well does the UN-fine-tuned model actually
do on our labeled tool-calling examples, called for real against Ollama?

Why this exists: stages 1-3 built and labeled a dataset assuming the
fine-tune was clearly needed, but two things changed since (see the parent
project's own git history, 2026-09-08):

  1. `OllamaProvider` was silently loading the model at Ollama's own 4096
     default context window regardless of what the model actually supports
     — any prompt past that got truncated from the front, not errored, and
     that's a fully plausible ROOT CAUSE for the hedging/no-tool-call
     behavior the README blames on "small model not reliably following
     instructions." `num_ctx` is now set explicitly (see MindConfig.NumCtx)
     and confirmed against real generated examples: the largest one
     measured 2976 tokens, comfortably under 4096.
  2. `analytics.local` (the mDNS hostname) was costing ~5.1s of pure DNS
     resolver dead time on EVERY request (NXDOMAIN on real DNS, then an
     mDNS fallback) — two sequential calls per NPC turn under the old
     think-then-act shape, ~10s of nothing before inference even started.
     mind.local.json now points at the bare IP instead.

Both were real, measured infrastructure bugs, not model-capability limits.
It's entirely possible the baseline is already much better than what the
logs that motivated this fine-tune showed, in which case the fine-tune
might not be worth the GPU time at all. Run this BEFORE stage 4, not after.

What this measures, and what it deliberately doesn't:
  - Calls Ollama exactly the way OllamaProvider.ChatInternal does: same
    body shape (model/stream/keep_alive/options.temperature/options.num_ctx
    /messages/tools), same endpoint, structured tool_calls parsed the same
    "arguments already a JSON object" way. Temperature is reconstructed
    per-NPC from common.PROFILES using the exact Personality.Temperature
    formula, not a single fixed value for every call.
  - DOES replicate Mind.ParseToolCall's LenientParseFromText fallback now
    (added 2026-09-08, see lenient_parse() below) — a real, observed case
    during this session's follow investigation: the model correctly chose
    follow, target_id "Alex," EMOTION and all, but wrote it as malformed
    JSON in the plain content field ({"name": follow, "parameters": ...} —
    unquoted name, wrong key) instead of Ollama's structured tool_calls.
    Scoring that as a flat miss (which this script did before) understated
    real accuracy for exactly the cases LenientParseFromText exists to
    recover — this was a measurement gap, not a finding about the model.
    Still NOT replicated: the one-retry-on-malformed-response loop
    Decide()/DecidePlayerRequest() do — a single bad attempt here stays
    scored as a miss rather than getting a second try, so this is still a
    same-direction (if smaller) undercount of true in-game accuracy.
  - ambient-mode examples already have "Your plan: ..." baked into their
    `user` text by 1_generate_scenarios.py — the real one-call act() step
    this reproduces, not a synthesized shortcut.

Usage:
    python 5_baseline_eval.py                      # everything (train+val)
    python 5_baseline_eval.py --val-only            # held-out set only
    python 5_baseline_eval.py --model openrpg-npc-llama3.2-3b  # post-tune,
                                                       # same script either way
    python 5_baseline_eval.py --limit 20            # smoke test first
"""
from __future__ import annotations

import argparse
import concurrent.futures
import json
import re
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

from common import PROFILES, VALID_ACTIONS

DATA_DIR = Path(__file__).parent / "data"

# Same formula as scripts/Personality.cs's Temperature property — kept in
# sync by hand, same posture as every other ported constant in common.py.
_NAME_TO_OC_N = {name: (o, n) for name, _, o, _, _, _, n, _ in PROFILES}


def temperature_for(system_text: str) -> float:
    # search, not match — persona_line() (2026-09-08 clean-prompt rewrite)
    # puts this after a "BACKGROUND: " label instead of at position 0, so
    # an anchored match() silently missed every call and fell through to
    # the 0.6 fallback below for every NPC, regardless of personality.
    m = re.search(r"You are (\w+)", system_text)
    if m and m.group(1) in _NAME_TO_OC_N:
        openness, neuroticism = _NAME_TO_OC_N[m.group(1)]
        return max(0.35, min(0.95, 0.35 + openness * 0.3 + neuroticism * 0.25))
    return 0.6  # unrecognized name (shouldn't happen against our own data) — a reasonable mid-point


# --- Faithful port of Mind.cs's LenientParseFromText/MatchesIntent/
# FindMentionedId/AllKnownTargetIds (see their own headers there for the
# full reasoning) — a small local model that skips structured tool-calling
# and just narrates its intent in plain prose is common enough that real
# Mind.cs recovers it rather than treating it as a hard failure; this eval
# needs the same recovery to measure real production accuracy.

HEDGE_WORDS = [
    "not", "never", "no", "n't", "don't", "doesn't", "didn't", "won't", "wouldn't",
    "shouldn't", "can't", "couldn't", "isn't", "aren't",
    "maybe", "might", "perhaps", "possibly", "unsure",
]

# The five actions BuildAction defaults to the nearest real instance when
# the recovered text never names which one — same list as Mind.BuildAction's
# own switch, since spoken/narrated prose essentially never contains a
# literal id like "tree_3."
GENERIC_DEFAULT_ACTIONS = {"pick_apple", "catch_fish", "gather_pinecone", "gather_berry", "pick_up_stick"}


def matches_intent(content: str, word: str) -> bool:
    m = re.search(rf"\b{re.escape(word)}\b", content, re.IGNORECASE)
    if not m:
        return False
    before = content[max(0, m.start() - 25):m.start()]
    if any(re.search(rf"\b{re.escape(h)}\b\W*$", before, re.IGNORECASE) for h in HEDGE_WORDS):
        return False
    after = content[m.end():m.end() + 15]
    return "?" not in after


def find_mentioned_id(content: str, candidates) -> str:
    for candidate in sorted({c for c in candidates if c}, key=len, reverse=True):
        if candidate.lower() in content.lower():
            return candidate
    return ""


def all_known_ids_from_tools(tools: list[dict]) -> set[str]:
    ids: set[str] = {"home", "firepit"}
    for t in tools:
        target_id_schema = t["function"]["parameters"]["properties"].get("target_id")
        if target_id_schema and "enum" in target_id_schema:
            ids.update(target_id_schema["enum"])
    return ids


def first_enum_for_action(tools: list[dict], action_name: str) -> str:
    for t in tools:
        if t["function"]["name"] == action_name:
            enum = t["function"]["parameters"]["properties"].get("target_id", {}).get("enum")
            return enum[0] if enum else ""
    return ""


def lenient_parse(content: str, tools: list[dict]) -> dict | None:
    """Returns {"name": ..., "arguments": {"target_id": ...}} recovered from
    plain prose, or None if nothing recognizable is in there — same
    generous-but-guarded scan as Mind.LenientParseFromText, longest action
    name first, "speak" only as the last resort.
    """
    if not content or not content.strip():
        return None

    name = None
    for action in sorted((a for a in VALID_ACTIONS if a != "speak"), key=len, reverse=True):
        if matches_intent(content, action):
            name = action
            break
    if name is None and matches_intent(content, "speak"):
        name = "speak"
    if name is None:
        return None

    target_id = find_mentioned_id(content, all_known_ids_from_tools(tools))
    if target_id == "" and name in GENERIC_DEFAULT_ACTIONS:
        target_id = first_enum_for_action(tools, name)

    return {"name": name, "arguments": {"target_id": target_id}}


def load_examples(paths: list[Path]) -> list[dict]:
    examples = []
    for path in paths:
        with path.open() as f:
            for line in f:
                line = line.strip()
                if line:
                    examples.append(json.loads(line))
    return examples


def call_ollama(base_url: str, model: str, num_ctx: int, timeout: float, seed: int | None,
                 system: str, user: str, tools: list[dict]) -> dict:
    """Same request shape as OllamaProvider.ChatInternal. Returns
    {"ok": True, "tool_call": {"name": ..., "arguments": {...}} | None,
     "content": str} or {"ok": False, "error": str}.
    """
    options = {"temperature": temperature_for(system), "num_ctx": num_ctx}
    if seed is not None:
        options["seed"] = seed
    body = {
        "model": model,
        "stream": False,
        "keep_alive": "30m",
        "options": options,
        "messages": [
            {"role": "system", "content": system},
            {"role": "user", "content": user},
        ],
    }
    if tools:
        body["tools"] = tools

    req = urllib.request.Request(
        f"{base_url.rstrip('/')}/api/chat",
        data=json.dumps(body).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            payload = json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        return {"ok": False, "error": f"http_{e.code}"}
    except urllib.error.URLError as e:
        return {"ok": False, "error": f"connection_failed_{e.reason}"}
    except (TimeoutError, OSError) as e:
        return {"ok": False, "error": f"timeout_{e}"}
    except json.JSONDecodeError:
        return {"ok": False, "error": "bad_json"}

    if payload.get("error"):
        return {"ok": False, "error": f"api_error_{payload['error']}"}
    message = payload.get("message")
    if message is None:
        return {"ok": False, "error": "bad_message_shape"}

    tool_calls = message.get("tool_calls") or []
    tool_call = None
    if tool_calls:
        fn = tool_calls[0].get("function") or {}
        if fn.get("name"):
            tool_call = {"name": fn["name"], "arguments": fn.get("arguments") or {}}
    return {"ok": True, "tool_call": tool_call, "content": message.get("content") or ""}


def eval_one(args, example: dict) -> dict:
    result = call_ollama(
        args.base_url, args.model, args.num_ctx, args.timeout, args.seed,
        example["system"], example["user"], example["tools"],
    )
    expected_name = example["response"]["name"]
    expected_target = example["response"].get("arguments", {}).get("target_id")

    recovered_leniently = False
    if not result["ok"]:
        predicted_name = f"ERROR:{result['error']}"
        predicted_target = None
    elif result["tool_call"] is not None:
        predicted_name = result["tool_call"]["name"]
        predicted_target = result["tool_call"]["arguments"].get("target_id")
        if predicted_name not in VALID_ACTIONS:
            predicted_name = f"INVALID:{predicted_name}"
    else:
        lenient = lenient_parse(result["content"], example["tools"])
        if lenient is not None:
            predicted_name = lenient["name"]
            predicted_target = lenient["arguments"].get("target_id")
            recovered_leniently = True
        else:
            predicted_name = "NO_TOOL_CALL"
            predicted_target = None

    return {
        "mode": example["mode"],
        "tags": example.get("tags", []),
        "expected": expected_name,
        "predicted": predicted_name,
        "expected_target": expected_target,
        "predicted_target": predicted_target,
        "correct": predicted_name == expected_name,
        "target_correct": (predicted_target == expected_target) if expected_target is not None else None,
        "recovered_leniently": recovered_leniently,
    }


def print_confusion_matrix(results: list[dict]) -> None:
    expected_labels = sorted({r["expected"] for r in results})
    predicted_labels = sorted({r["predicted"] for r in results})
    # Keep the matrix to labels that actually occurred as EXPECTED across
    # rows, but predicted-only labels (NO_TOOL_CALL, ERROR:*, a wrong
    # action never expected anywhere) still need their own columns.
    all_cols = sorted(set(expected_labels) | set(predicted_labels))

    counts: dict[tuple[str, str], int] = {}
    for r in results:
        key = (r["expected"], r["predicted"])
        counts[key] = counts.get(key, 0) + 1

    col_width = max(6, max((len(c) for c in all_cols), default=6))
    row_label_width = max((len(e) for e in expected_labels), default=8)

    header = " " * (row_label_width + 2) + "".join(f"{c[:col_width]:>{col_width + 1}}" for c in all_cols)
    print(header)
    for e in expected_labels:
        row = f"{e:<{row_label_width}}  " + "".join(
            f"{counts.get((e, c), 0):>{col_width + 1}}" for c in all_cols
        )
        print(row)


def print_report(results: list[dict]) -> None:
    total = len(results)
    correct = sum(1 for r in results if r["correct"])
    print(f"\n=== Overall: {correct}/{total} correct ({correct / total:.1%}) ===\n")

    print("--- Confusion matrix (rows = expected action, cols = predicted) ---")
    print_confusion_matrix(results)

    print("\n--- By mode ---")
    for mode in sorted({r["mode"] for r in results}):
        subset = [r for r in results if r["mode"] == mode]
        c = sum(1 for r in subset if r["correct"])
        print(f"  {mode:<16} {c}/{len(subset)} ({c / len(subset):.1%})")

    print("\n--- Per-expected-action recall ---")
    for action in sorted({r["expected"] for r in results}):
        subset = [r for r in results if r["expected"] == action]
        c = sum(1 for r in subset if r["correct"])
        print(f"  {action:<20} {c}/{len(subset)} ({c / len(subset):.1%})")

    with_target = [r for r in results if r["target_correct"] is not None and r["correct"]]
    if with_target:
        tc = sum(1 for r in with_target if r["target_correct"])
        print(f"\n--- target_id match rate (given the right action) ---")
        print(f"  {tc}/{len(with_target)} ({tc / len(with_target):.1%})")

    no_call = sum(1 for r in results if r["predicted"] == "NO_TOOL_CALL")
    errors = sum(1 for r in results if r["predicted"].startswith("ERROR:"))
    leniently_recovered = [r for r in results if r["recovered_leniently"]]
    if no_call or errors or leniently_recovered:
        print(f"\n--- Non-tool-call outcomes ---")
        print(f"  NO_TOOL_CALL (no structured call, no recoverable intent in prose either): {no_call}")
        print(f"  ERROR (connection/HTTP/parse failure):                                    {errors}")
        if leniently_recovered:
            lc = sum(1 for r in leniently_recovered if r["correct"])
            print(f"  Recovered via lenient prose parse: {len(leniently_recovered)} "
                  f"({lc} scored correct once recovered) — these would ALSO have been NO_TOOL_CALL "
                  f"under the old strict-only scoring.")


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--base-url", default="http://analytics.local:11434",
                    help="Ollama endpoint. The hostname pays a real, reproducible ~5.1s/request DNS "
                         "resolution cost on some networks (see MindConfig.BaseUrl's own comment) — "
                         "pass your own resolved LAN IP here instead (--base-url http://<ip>:11434) "
                         "if that matters for your setup; not hardcoded here since that IP is "
                         "specific to one machine's network, not portable across setups.")
    p.add_argument("--model", default="llama3.2:3b", help="Ollama model tag to eval")
    p.add_argument("--num-ctx", type=int, default=4096, help="matches MindConfig.NumCtx's current tuned value")
    p.add_argument("--timeout", type=float, default=60.0)
    p.add_argument("--concurrency", type=int, default=4, help="matches GameSettings.MaxConcurrentLlmRequests")
    p.add_argument("--seed", type=int, default=None, help="Ollama options.seed, for a reproducible baseline run")
    p.add_argument("--val-only", action="store_true", help="eval data/val.jsonl only (the held-out split)")
    p.add_argument("--data", nargs="+", type=Path, default=None,
                    help="explicit jsonl file(s) to eval; default is train.jsonl+val.jsonl (everything, "
                         "since nothing's been trained on yet)")
    p.add_argument("--limit", type=int, default=None, help="only eval the first N examples (smoke test)")
    p.add_argument("--out", type=Path, default=None, help="also write raw per-example results as JSON here")
    args = p.parse_args()

    if args.data:
        paths = args.data
    elif args.val_only:
        paths = [DATA_DIR / "val.jsonl"]
    else:
        paths = [DATA_DIR / "train.jsonl", DATA_DIR / "val.jsonl"]

    examples = load_examples(paths)
    if args.limit is not None:
        examples = examples[: args.limit]
    print(f"Evaluating {len(examples)} examples from {[str(p) for p in paths]}")
    print(f"model={args.model} base_url={args.base_url} num_ctx={args.num_ctx} concurrency={args.concurrency}")

    start = time.time()
    results = [None] * len(examples)
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.concurrency) as pool:
        futures = {pool.submit(eval_one, args, ex): i for i, ex in enumerate(examples)}
        done = 0
        for future in concurrent.futures.as_completed(futures):
            i = futures[future]
            results[i] = future.result()
            done += 1
            if done % 10 == 0 or done == len(examples):
                print(f"  {done}/{len(examples)}...", file=sys.stderr)
    elapsed = time.time() - start
    print(f"Done in {elapsed:.1f}s ({elapsed / len(examples):.2f}s/example avg, concurrency={args.concurrency})")

    print_report(results)

    if args.out:
        args.out.write_text(json.dumps(results, indent=2))
        print(f"\nRaw results written to {args.out}")


if __name__ == "__main__":
    main()
