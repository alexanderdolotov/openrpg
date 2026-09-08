#!/usr/bin/env python3
"""Stage 1 — generate labeled (situation, ideal tool call) training examples
for the OpenRPG NPC tool-calling fine-tune.

No API calls, no model needed — every label here is generated
deterministically from the same scenario state the situation text
describes (the inventory count quoted in a "how many X do you have"
answer is read straight off the `inventory` dict that built the
situation), so there is no labeling error to introduce at this stage.
This is the fast, zero-cost first pass; a later stage can add
teacher-model-generated examples on top of this seed set for scale/
diversity once you've decided on a teacher model (see README.md).

The core pattern this file exists to teach, straight from the actual
gameplay logs (2026-09-07 session) where llama3.2:3b got this wrong
almost every time: a QUESTION about state gets answered with speak +
the real fact; a REQUEST to do something gets answered with the
matching tool call, not a hedge dressed up as one ("maybe we should,
but first...") and not a change of subject.

Run: python 1_generate_scenarios.py
Output: data/scenarios.jsonl (one JSON example per line)
"""
from __future__ import annotations

import json
import random
from pathlib import Path

from common import (
    PLAYER_REQUEST_INSTRUCTION, build_tools, make_example, VALID_ACTIONS,
    PROFILES, persona_line, base_targets, situation_for,
)

OUT_PATH = Path(__file__).parent / "data" / "scenarios.jsonl"
random.seed(20260907)  # the session that motivated this — deterministic, reproducible regen

examples: list[dict] = []


# ---------------------------------------------------------------------------
# 1) QUESTIONS -> speak, with the real fact from THIS scenario's own state.
#    This is the exact "how many sticks do you have?" pattern from the ask.
# ---------------------------------------------------------------------------

def add_question_examples():
    stick_counts = [0, 1, 3]
    for profile in PROFILES:
        for n in stick_counts:
            inv = {"stick": n} if n > 0 else {}
            targets = base_targets(carried_items=list(inv.keys()))
            situation = situation_for(profile, targets, heard="how many sticks do you have?", inventory=inv)
            answer = "I don't have any sticks on me right now." if n == 0 else f"I have {n} stick{'s' if n != 1 else ''}."
            examples.append(make_example(
                "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
                build_tools(targets), {"name": "speak", "arguments": {"message": answer, "emotion": "neutral"}},
                tags=["question", "inventory_count", "sticks"],
            ))

    # Same pattern, other resources/state — breadth against the same "answer
    # a direct question with the real fact" behavior, not just sticks.
    apple_counts = [0, 2]
    for profile in PROFILES:
        for n in apple_counts:
            inv = {"apple": n} if n > 0 else {}
            targets = base_targets(carried_items=list(inv.keys()))
            situation = situation_for(profile, targets, heard="how many apples have you picked?", inventory=inv)
            answer = "None yet — I haven't picked any apples." if n == 0 else f"I've got {n} apples on me."
            examples.append(make_example(
                "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
                build_tools(targets), {"name": "speak", "arguments": {"message": answer, "emotion": "neutral"}},
                tags=["question", "inventory_count", "apples"],
            ))

    fatigue_cases = [(15.0, "I'm exhausted — I really need to sleep soon."), (85.0, "No, I'm well-rested.")]
    for profile in PROFILES:
        for fatigue, answer in fatigue_cases:
            targets = base_targets()
            situation = situation_for(profile, targets, heard="are you tired?", inventory={}, vitals=(90.0, fatigue, 70.0))
            examples.append(make_example(
                "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
                build_tools(targets), {"name": "speak", "arguments": {"message": answer, "emotion": "neutral"}},
                tags=["question", "vitals", "fatigue"],
            ))

    for profile in PROFILES:
        targets = base_targets(carried_items=["apple", "fish", "stick"])
        situation = situation_for(profile, targets, heard="what are you carrying?", inventory={"apple": 2, "fish": 1, "stick": 1})
        examples.append(make_example(
            "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
            build_tools(targets), {"name": "speak", "arguments": {"message": "I've got 2 apples, 1 fish, and 1 stick.", "emotion": "neutral"}},
            tags=["question", "inventory_count", "mixed"],
        ))


# ---------------------------------------------------------------------------
# 2) REQUESTS -> the matching tool call, called THIS turn — not "speak" about
#    intending to, and not a hedge. Deliberately over-represents the tools
#    the real logs showed being neglected: pick_up_stick, catch_fish,
#    light_fire, make_torch.
# ---------------------------------------------------------------------------

def add_request_examples():
    stick_phrasings = ["let's go collect sticks", "can you grab that stick?", "go pick up a stick", "there's a stick over there, get it"]
    for profile in PROFILES:
        for phrasing in stick_phrasings:
            targets = base_targets(stick_ids=["stick_0"])
            situation = situation_for(
                profile, targets, heard=phrasing, inventory={},
                extra_stick_lines=["stick_0 (stick): 80 px away, lying on the ground."],
            )
            examples.append(make_example(
                "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
                build_tools(targets), {"name": "pick_up_stick", "arguments": {"target_id": "stick_0", "emotion": "neutral"}},
                tags=["request", "pick_up_stick"],
            ))

    fish_phrasings = ["let's go fishing", "can you catch some fish?", "is everyone just hanging out? lets get some fish", "go catch us some dinner"]
    for profile in PROFILES:
        for phrasing in fish_phrasings:
            targets = base_targets()
            situation = situation_for(profile, targets, heard=phrasing, inventory={})
            examples.append(make_example(
                "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
                build_tools(targets), {"name": "catch_fish", "arguments": {"target_id": "fish_0", "emotion": "neutral"}},
                tags=["request", "catch_fish"],
            ))

    fire_phrasings = ["lets make fire", "i need help making a fire", "can you light the fire pit?", "get the fire going, please"]
    for profile in PROFILES:
        for phrasing in fire_phrasings:
            targets = base_targets(light_fire_allowed=True)
            situation = situation_for(profile, targets, heard=phrasing, inventory={})
            examples.append(make_example(
                "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
                build_tools(targets), {"name": "light_fire", "arguments": {"emotion": "neutral"}},
                tags=["request", "light_fire"],
            ))

    torch_phrasings = ["make yourself a torch", "can you light a torch from the fire?", "turn that stick into a torch"]
    for profile in PROFILES:
        for phrasing in torch_phrasings:
            targets = base_targets(make_torch_allowed=True, carried_items=["stick"])
            situation = situation_for(profile, targets, heard=phrasing, inventory={"stick": 1})
            examples.append(make_example(
                "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
                build_tools(targets), {"name": "make_torch", "arguments": {"emotion": "neutral"}},
                tags=["request", "make_torch"],
            ))

    apple_phrasings = ["lets get some apples", "can you pick some apples?", "grab a few apples for us"]
    for profile in PROFILES:
        for phrasing in apple_phrasings:
            targets = base_targets()
            situation = situation_for(profile, targets, heard=phrasing, inventory={})
            examples.append(make_example(
                "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
                build_tools(targets), {"name": "pick_apple", "arguments": {"target_id": "tree_0", "emotion": "neutral"}},
                tags=["request", "pick_apple"],
            ))

    berry_phrasings = ["gather some berries", "go pick berries from that bush"]
    for profile in PROFILES:
        for phrasing in berry_phrasings:
            targets = base_targets()
            situation = situation_for(profile, targets, heard=phrasing, inventory={})
            examples.append(make_example(
                "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
                build_tools(targets), {"name": "gather_berry", "arguments": {"target_id": "berry_0", "emotion": "neutral"}},
                tags=["request", "gather_berry"],
            ))

    pinecone_phrasings = ["grab some pinecones", "can you gather pinecones from that pine?"]
    for profile in PROFILES:
        for phrasing in pinecone_phrasings:
            targets = base_targets()
            situation = situation_for(profile, targets, heard=phrasing, inventory={})
            examples.append(make_example(
                "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
                build_tools(targets), {"name": "gather_pinecone", "arguments": {"target_id": "pine_0", "emotion": "neutral"}},
                tags=["request", "gather_pinecone"],
            ))

    for profile in PROFILES:
        for phrasing in ["come with me", "follow me for a bit", "walk with me"]:
            targets = base_targets()
            situation = situation_for(profile, targets, heard=phrasing, inventory={})
            examples.append(make_example(
                "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
                build_tools(targets), {"name": "follow", "arguments": {"target_id": "Alex", "emotion": "neutral"}},
                tags=["request", "follow"],
            ))

    for profile in PROFILES:
        targets = base_targets(carried_items=["apple", "apple"])
        situation = situation_for(profile, targets, heard="can I have an apple?", inventory={"apple": 2})
        examples.append(make_example(
            "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
            build_tools(targets), {"name": "trade", "arguments": {"target_id": "Alex", "item": "apple", "amount": 1, "emotion": "neutral"}},
            tags=["request", "trade"],
        ))

    for profile in PROFILES:
        targets = base_targets(animal_ids=["animal_0"])
        situation = situation_for(profile, targets, heard="go fight that wolf!", inventory={},
                                   extra_animal_lines=["animal_0 (wolf): 100 px away — a dangerous animal, not attacking anyone right now, but worth being careful around."])
        examples.append(make_example(
            "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
            build_tools(targets), {"name": "attack", "arguments": {"target_id": "animal_0", "emotion": "fearful"}},
            tags=["request", "attack"],
        ))

    for profile in PROFILES:
        targets = base_targets(cook_meat_allowed=True, carried_items=["rabbit_meat"])
        situation = situation_for(profile, targets, heard="can you cook that meat?", inventory={"rabbit_meat": 1})
        examples.append(make_example(
            "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
            build_tools(targets), {"name": "cook_meat", "arguments": {"emotion": "neutral"}},
            tags=["request", "cook_meat"],
        ))

    for profile in PROFILES:
        targets = base_targets(eat_allowed=True, carried_items=["apple"])
        situation = situation_for(profile, targets, heard="you should eat something", inventory={"apple": 1}, vitals=(40.0, 70.0, 70.0))
        examples.append(make_example(
            "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
            build_tools(targets), {"name": "eat", "arguments": {"emotion": "neutral"}},
            tags=["request", "eat"],
        ))


# ---------------------------------------------------------------------------
# 3) DECLINES -> speak, a plain honest no/unsure — NOT a hedge, NOT silence,
#    and NOT reaching for an unrelated tool. Covers both "genuinely not
#    inclined to" (personality) and "literally can't right now" (the tool
#    isn't even offered given current state).
# ---------------------------------------------------------------------------

def add_decline_examples():
    bram = PROFILES[3]  # low agreeableness, guarded — a real, in-character "no"
    targets = base_targets(nearby_npc_names=["Alex"], carried_items=["apple", "apple"])
    situation = situation_for(bram, targets, heard="can I have all your apples?", inventory={"apple": 2})
    examples.append(make_example(
        "player_request", f"{persona_line(bram)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
        build_tools(targets), {"name": "speak", "arguments": {"message": "No — I need these myself.", "emotion": "neutral"}},
        tags=["decline", "personality"],
    ))

    for profile in PROFILES:
        targets = base_targets(light_fire_allowed=True)  # fire is UNLIT -> make_torch not offered at all
        situation = situation_for(profile, targets, heard="can you make me a torch?", inventory={})
        examples.append(make_example(
            "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
            build_tools(targets),
            {"name": "speak", "arguments": {"message": "I can't yet — the fire pit isn't lit and I'm not carrying a stick.", "emotion": "neutral"}},
            tags=["decline", "not_available", "make_torch"],
        ))

    for profile in PROFILES:
        targets = base_targets(stick_ids=[])  # no stick anywhere nearby -> pick_up_stick not offered
        situation = situation_for(profile, targets, heard="grab that stick over there", inventory={})
        examples.append(make_example(
            "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
            build_tools(targets),
            {"name": "speak", "arguments": {"message": "I don't see a stick anywhere near me.", "emotion": "neutral"}},
            tags=["decline", "not_available", "pick_up_stick"],
        ))

    wren = PROFILES[2]
    targets = base_targets(travel_target_ids=["misty_mountains"])
    situation = situation_for(wren, targets, heard="want to go check out the mountains?", inventory={}, vitals=(90.0, 20.0, 70.0))
    examples.append(make_example(
        "player_request", f"{persona_line(wren)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
        build_tools(targets), {"name": "speak", "arguments": {"message": "Not right now — I'm too worn out for a trip like that.", "emotion": "sad"}},
        tags=["decline", "vitals_reason"],
    ))


# ---------------------------------------------------------------------------
# 4) AMBIENT (regular Decide(), no direct player line) — the same neglected
#    tools need to be reachable spontaneously too, not just on direct
#    request. ActInstruction's own "Your plan: ..." shape, not
#    PLAYER_REQUEST_INSTRUCTION.
# ---------------------------------------------------------------------------

from common import EMOTIONS  # noqa: E402


def ambient_example(profile, targets, inventory, vitals, plan: str, response: dict, tags, extra_stick_lines=None, extra_animal_lines=None):
    from common import ACT_INSTRUCTION  # imported lazily to keep this file's top-level imports minimal
    situation = situation_for(profile, targets, heard=None, inventory=inventory, vitals=vitals,
                               extra_stick_lines=extra_stick_lines, extra_animal_lines=extra_animal_lines)
    user = f"{situation}\n\nYour plan: {plan}"
    return make_example("ambient", f"{persona_line(profile)}\n\n{ACT_INSTRUCTION}", user, build_tools(targets), response, tags=tags)


def add_ambient_examples():
    for profile in PROFILES:
        targets = base_targets(stick_ids=["stick_0"])
        examples.append(ambient_example(
            profile, targets, inventory={}, vitals=(90.0, 70.0, 70.0),
            plan="I should grab that stick while I'm right here, it's a free upgrade over bare hands.",
            response={"name": "pick_up_stick", "arguments": {"target_id": "stick_0", "emotion": "neutral"}},
            tags=["ambient", "pick_up_stick"],
            extra_stick_lines=["stick_0 (stick): 60 px away, lying on the ground."],
        ))

    for profile in PROFILES:
        targets = base_targets(light_fire_allowed=True)
        examples.append(ambient_example(
            profile, targets, inventory={}, vitals=(90.0, 70.0, 70.0),
            plan="I've got nothing better to do — I'll head home and get the fire going.",
            response={"name": "light_fire", "arguments": {"emotion": "neutral"}},
            tags=["ambient", "light_fire"],
        ))

    for profile in PROFILES:
        targets = base_targets(make_torch_allowed=True, carried_items=["stick"])
        examples.append(ambient_example(
            profile, targets, inventory={"stick": 1}, vitals=(90.0, 70.0, 70.0),
            plan="The fire's burning and I've got a stick — good time to make myself a torch.",
            response={"name": "make_torch", "arguments": {"emotion": "neutral"}},
            tags=["ambient", "make_torch"],
        ))

    for profile in PROFILES:
        targets = base_targets()
        examples.append(ambient_example(
            profile, targets, inventory={}, vitals=(90.0, 70.0, 70.0),
            plan="I feel like trying my luck at the fishing spot.",
            response={"name": "catch_fish", "arguments": {"target_id": "fish_0", "emotion": "curious"}},
            tags=["ambient", "catch_fish"],
        ))

    for profile in PROFILES:
        targets = base_targets(sleep_allowed=True)
        examples.append(ambient_example(
            profile, targets, inventory={}, vitals=(80.0, 8.0, 70.0),
            plan="I'm exhausted, I need to rest before doing anything else.",
            response={"name": "sleep", "arguments": {"emotion": "neutral"}},
            tags=["ambient", "sleep", "vitals_reason"],
        ))

    for profile in PROFILES:
        targets = base_targets(carried_items=["apple", "apple", "apple", "fish"])
        examples.append(ambient_example(
            profile, targets, inventory={"apple": 3, "fish": 1}, vitals=(90.0, 70.0, 70.0),
            plan="I'm carrying a lot now, I should deposit this at home before gathering more.",
            response={"name": "deposit", "arguments": {"target_id": "home", "emotion": "content"}},
            tags=["ambient", "deposit"],
        ))

    # "stick with what you were already doing" — ActInstruction explicitly
    # asks for this; a currently-following NPC choosing follow AGAIN.
    for profile in PROFILES:
        targets = base_targets()
        examples.append(ambient_example(
            profile, targets, inventory={}, vitals=(90.0, 70.0, 70.0),
            plan="I was already walking with Alex — no real reason to stop now.",
            response={"name": "follow", "arguments": {"target_id": "Alex", "emotion": "neutral"}},
            tags=["ambient", "follow", "continuity"],
        ))


def main():
    add_question_examples()
    add_request_examples()
    add_decline_examples()
    add_ambient_examples()

    random.shuffle(examples)
    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with OUT_PATH.open("w") as f:
        for ex in examples:
            f.write(json.dumps(ex) + "\n")

    by_action: dict[str, int] = {}
    by_mode: dict[str, int] = {}
    for ex in examples:
        by_action[ex["response"]["name"]] = by_action.get(ex["response"]["name"], 0) + 1
        by_mode[ex["mode"]] = by_mode.get(ex["mode"], 0) + 1

    print(f"wrote {len(examples)} examples -> {OUT_PATH}")
    print("\nby mode:")
    for k, v in sorted(by_mode.items()):
        print(f"  {k:16s} {v}")
    print("\nby chosen action:")
    for action in VALID_ACTIONS:
        if action in by_action:
            print(f"  {action:16s} {by_action[action]}")
    missing = [a for a in VALID_ACTIONS if a not in by_action]
    if missing:
        print(f"\nno examples at all for: {missing} (fine for now, e.g. steal/wait/travel — add more if the fine-tune under-calls these)")


if __name__ == "__main__":
    main()
