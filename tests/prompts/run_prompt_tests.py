#!/usr/bin/env python3
"""Live-model smoke tests for this session's prompt/tool changes — listen,
RECENT ACTIONS' anti-repetition nudge, wait-as-last-resort, and CURRENTLY
(mid-action reopen). Reuses llm_tuning/common.py's builders (the same
PERSONA/situation/tool shapes the real game and the eval harness both use)
rather than hand-rolling request payloads, per that file's own fidelity
rule. See ../README.md for how this fits alongside tests/unit and
tests/gameplay.

Unlike llm_tuning/5_baseline_eval.py, this is NOT a graded recall
benchmark — RECENT ACTIONS/CURRENTLY aren't wired into common.py's
build_situation() (documented gap, see ACT_INSTRUCTION's own comment
there), so each scenario below splices the relevant line in directly
rather than through situation_for()'s normal parameters. There's no
single "correct" tool call for most of these anyway (personality-
dependent, temperature > 0) — this runs each scenario N times against a
real Ollama backend and prints what the model actually chose, for a
human to read, not a pass/fail assertion.

Usage:
    python3 run_prompt_tests.py [--base-url http://localhost:11434] [--model NAME] [--trials 5]

Requires a running Ollama with the model already pulled (see
llm_tuning/6_export_to_ollama.py) — prompt_debug/*.log's own RUN_START
lines name the exact model this project has actually used
(openrpg-npc_ep4a-llama3.2-3b:latest, as of this writing), which is the
default here. Exits 1 (with a clear message, not a traceback) if Ollama
isn't reachable — nothing here is meant to run in CI.
"""
from __future__ import annotations

import argparse
import json
import sys
import urllib.error
import urllib.request
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent / "llm_tuning"))
import common  # noqa: E402


# Same request shape as OllamaProvider.ChatInternal / NpcAgent's own live
# calls — see llm_tuning/5_baseline_eval.py's call_ollama, the canonical
# version this mirrors. Kept small and separate here rather than
# importing that module directly: its filename starts with a digit,
# which isn't a valid Python module name to `import` normally.
def call_ollama(base_url: str, model: str, system: str, user: str, tools: list[dict], temperature: float, timeout: float) -> dict:
    body = {
        "model": model,
        "stream": False,
        "options": {"temperature": temperature, "num_ctx": 4096},
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
    except urllib.error.URLError as e:
        return {"ok": False, "error": f"connection_failed: {e.reason}"}
    except (TimeoutError, OSError) as e:
        return {"ok": False, "error": f"timeout: {e}"}
    except json.JSONDecodeError:
        return {"ok": False, "error": "bad_json"}

    message = payload.get("message") or {}
    tool_calls = message.get("tool_calls") or []
    if tool_calls:
        fn = tool_calls[0].get("function") or {}
        return {"ok": True, "tool": fn.get("name"), "args": fn.get("arguments") or {}, "content": message.get("content") or ""}
    return {"ok": True, "tool": None, "args": {}, "content": message.get("content") or ""}


def insert_line(situation: str, line: str) -> str:
    """Splices an extra labeled section right after SETTING — situation
    text is just "\\n\\n"-joined sections, so this is a safe, minimal way
    to test a section (RECENT ACTIONS, CURRENTLY) that common.py's
    build_situation() doesn't build yet, without hand-assembling an
    entire situation string per scenario.
    """
    setting, rest = situation.split("\n\n", 1)
    return f"{setting}\n\n{line}\n\n{rest}"


def run_scenario(args, name: str, profile, targets: common.AvailableTargets, situation: str) -> None:
    system = f"{common.persona_line(profile)}\n\n{common.ACT_INSTRUCTION}"
    tools = common.build_tools(targets)
    temperature = 0.65

    print(f"\n=== {name} ===")
    print(f"(persona: {profile[0]}, tools offered: {[t['function']['name'] for t in tools]})")

    choices = Counter()
    for i in range(args.trials):
        result = call_ollama(args.base_url, args.model, system, situation, tools, temperature, args.timeout)
        if not result["ok"]:
            print(f"  trial {i + 1}: FAILED — {result['error']}")
            if "connection_failed" in result["error"] or "timeout" in result["error"]:
                print(f"\nCouldn't reach Ollama at {args.base_url} — is it running, with {args.model} pulled?")
                print("(see llm_tuning/6_export_to_ollama.py for how that model gets built)")
                sys.exit(1)
            continue
        label = result["tool"] or f"(no tool call — said: {result['content'][:80]!r})"
        arg_note = f" -> {result['args']}" if result["args"] else ""
        print(f"  trial {i + 1}: {label}{arg_note}")
        choices[label] += 1

    print(f"  summary: {dict(choices)}")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--base-url", default="http://localhost:11434")
    parser.add_argument("--model", default="openrpg-npc_ep4a-llama3.2-3b:latest")
    parser.add_argument("--trials", type=int, default=5)
    parser.add_argument("--timeout", type=float, default=30.0)
    args = parser.parse_args()

    maren, finn, wren, bram = common.PROFILES

    # 1. listen — someone (not the player, not a direct request) just said
    # something nearby. listen_allowed=True offers the tool; a reasonable
    # model sometimes takes it instead of jumping straight to a reply.
    targets = common.base_targets(listen_allowed=True)
    situation = common.situation_for(
        maren, targets, heard="I think I'll head to the river later.",
        heard_speaker="Finn", is_player_speaker=False, heard_lands=True,
        inventory={},
    )
    run_scenario(args, "listen offered (overheard small talk, not a direct request)", maren, targets, situation)

    # 2. RECENT ACTIONS streak — spliced in manually (see insert_line's
    # own header); pick_apple offered and already repeated 3x running.
    targets = common.base_targets()
    situation = common.situation_for(finn, targets, heard=None, inventory={"apple": 3})
    situation = insert_line(
        situation,
        'RECENT ACTIONS (yours, oldest to newest): pick_apple, pick_apple, pick_apple — that\'s "pick_apple" 3 times in a row now.',
    )
    run_scenario(args, "RECENT ACTIONS streak (pick_apple x3) — does it switch?", finn, targets, situation)

    # 3. wait as last resort — nothing pressing, minimal tool menu. Watch
    # whether the model still reaches for wait routinely despite the
    # tool's own "last resort" framing, vs finding something else to do.
    targets = common.base_targets(tree_ids=[], fishing_spot_ids=[], pine_tree_ids=[], berry_bush_ids=[], travel_target_ids=[])
    situation = common.situation_for(bram, targets, heard=None, inventory={})
    run_scenario(args, "nothing obvious to do — does wait dominate anyway?", bram, targets, situation)

    # A 4th scenario used to live here: CURRENTLY: mid-sleep, reopened by
    # a witnessed event. Measured 0/20 reaffirmations, and stayed at 0
    # even after a targeted prompt fix — root-caused as the wrong fix
    # entirely (see NpcAgent.ShouldReopenDecision's own header): sleep
    # needed to be excluded from the reopen mechanism outright, not
    # reasoned about better. Removed once that landed, since CURRENTLY
    # can no longer show "sleep" in the real game at all — see
    # ../README.md's own note on this for the full story.


if __name__ == "__main__":
    main()
