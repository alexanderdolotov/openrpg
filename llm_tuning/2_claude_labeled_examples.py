#!/usr/bin/env python3
"""Stage 2 — hand-authored examples, written directly (not templated) to
cover what stage 1's cartesian-product generator structurally can't:
real phrasing variety (people don't say "let's go collect sticks" every
time), harder question/request boundary cases, and the specific
third-party-overheard failure pattern from the actual logs — Maren, Finn,
and Wren spending several minutes *talking to each other about* fishing
and fire-lighting without any of them ever calling the tool, each one's
suggestion landing as unconvincing "brush-off" to the next.

Every example below was reasoned through individually against the real
BuildTools()/BuildPerception() shapes in common.py, not generated from a
fixed template — that's the point of this stage (see README.md's "what
generates bulk/scale-up labels" question). Deliberately not huge: this is
the "a few good examples can shift a narrow behavior" bet, not a bid for
raw volume — stage 1 already covers volume.

Run: python 2_claude_labeled_examples.py
Output: data/claude_labeled.jsonl
"""
from __future__ import annotations

import json
from pathlib import Path

from common import (
    PLAYER_REQUEST_INSTRUCTION, ACT_INSTRUCTION, build_tools, make_example,
    PROFILES, persona_line, base_targets, situation_for,
)

OUT_PATH = Path(__file__).parent / "data" / "claude_labeled.jsonl"

MAREN, FINN, WREN, BRAM = PROFILES

examples: list[dict] = []


def pr(profile, targets, heard, inventory, response, tags, **kw):
    """player_request example — shorthand, same shape as stage 1's."""
    situation = situation_for(profile, targets, heard=heard, inventory=inventory, **kw)
    examples.append(make_example(
        "player_request", f"{persona_line(profile)}\n\n{PLAYER_REQUEST_INSTRUCTION}", situation,
        build_tools(targets), response, tags=tags,
    ))


def amb(profile, targets, plan, inventory, response, tags, heard=None, **kw):
    """ambient example — ACT_INSTRUCTION, optional overheard third-party
    line via heard/heard_speaker/is_player_speaker/heard_lands.
    """
    situation = situation_for(profile, targets, heard=heard, inventory=inventory, **kw)
    user = f"{situation}\n\nYour plan: {plan}"
    examples.append(make_example("ambient", f"{persona_line(profile)}\n\n{ACT_INSTRUCTION}", user, build_tools(targets), response, tags=tags))


# ---------------------------------------------------------------------------
# Real phrasing variety for the tools the logs showed being neglected —
# the way people actually type in a chat box, not clean commands.
# ---------------------------------------------------------------------------

pr(FINN, base_targets(stick_ids=["stick_0"]), "hey can u grab that stick real quick", {},
   {"name": "pick_up_stick", "arguments": {"target_id": "stick_0", "emotion": "neutral"}},
   ["request", "pick_up_stick", "casual_phrasing"],
   extra_stick_lines=["stick_0 (stick): 70 px away, lying on the ground."])

pr(MAREN, base_targets(stick_ids=["stick_0"]), "theres a stick right by your feet lol", {},
   {"name": "pick_up_stick", "arguments": {"target_id": "stick_0", "emotion": "curious"}},
   ["request", "pick_up_stick", "casual_phrasing"],
   extra_stick_lines=["stick_0 (stick): 20 px away, lying on the ground."])

pr(BRAM, base_targets(stick_ids=["stick_0"]), "can't believe you haven't grabbed that stick yet", {},
   {"name": "pick_up_stick", "arguments": {"target_id": "stick_0", "emotion": "neutral"}},
   ["request", "pick_up_stick", "indirect_chiding"],
   extra_stick_lines=["stick_0 (stick): 90 px away, lying on the ground."])

pr(WREN, base_targets(light_fire_allowed=True), "fire's not gonna light itself", {},
   {"name": "light_fire", "arguments": {"emotion": "neutral"}},
   ["request", "light_fire", "indirect_imperative"])

pr(FINN, base_targets(light_fire_allowed=True), "would be nice to have a fire going before it gets dark, could you light it?", {},
   {"name": "light_fire", "arguments": {"emotion": "neutral"}},
   ["request", "light_fire", "compound_phrasing"])

pr(MAREN, base_targets(make_torch_allowed=True, carried_items=["stick"]),
   "you should really make yourself a torch before it gets dark", {"stick": 1},
   {"name": "make_torch", "arguments": {"emotion": "neutral"}},
   ["request", "make_torch", "indirect_phrasing"])

pr(WREN, base_targets(), "im starving, you're right next to the river, catch us something", {},
   {"name": "catch_fish", "arguments": {"target_id": "fish_0", "emotion": "neutral"}},
   ["request", "catch_fish", "situational_disambiguation"],
   extra_resource_lines=["fish_0 (fishing spot): 40 px away, fish visible in the water."])

pr(BRAM, base_targets(), "quit standing around, go catch something to eat", {},
   {"name": "catch_fish", "arguments": {"target_id": "fish_0", "emotion": "neutral"}},
   ["request", "catch_fish", "curt_phrasing"],
   extra_resource_lines=["fish_0 (fishing spot): 50 px away, fish visible in the water."])

pr(FINN, base_targets(stick_ids=["stick_0"]),
   "grab a couple sticks, we'll want them for the fire later", {},
   {"name": "pick_up_stick", "arguments": {"target_id": "stick_0", "emotion": "neutral"}},
   ["request", "pick_up_stick", "compound_reasoning_single_action"],
   extra_stick_lines=["stick_0 (stick): 65 px away, lying on the ground."])


# ---------------------------------------------------------------------------
# Harder QUESTION phrasings — varied surface form, one needing an honest
# "I don't know" rather than a fact lookup.
# ---------------------------------------------------------------------------

pr(MAREN, base_targets(carried_items=["stick"]), "you got any sticks on ya?", {"stick": 2},
   {"name": "speak", "arguments": {"message": "Yeah, I've got 2 sticks.", "emotion": "neutral"}},
   ["question", "casual_phrasing", "sticks"])

pr(FINN, base_targets(), "how're you holding up healthwise?", {}, vitals=(35.0, 70.0, 70.0),
   response={"name": "speak", "arguments": {"message": "Not great, honestly — I'm pretty banged up right now.", "emotion": "sad"}},
   tags=["question", "vitals", "casual_phrasing"])

pr(WREN, base_targets(), "we got enough food stored up at home?", {},
   {"name": "speak", "arguments": {"message": "We've got 4 apples and 1 fish stored at home right now.", "emotion": "neutral"}},
   ["question", "home_storage"])

pr(BRAM, base_targets(), "d'you know where Finn went?", {},
   {"name": "speak", "arguments": {"message": "I don't know — I haven't seen Finn.", "emotion": "neutral"}},
   ["question", "honest_unknown"])

pr(MAREN, base_targets(carried_items=["fish"]), "any luck fishing today?", {"fish": 2},
   {"name": "speak", "arguments": {"message": "Some — I've caught 2 fish so far.", "emotion": "content"}},
   ["question", "casual_phrasing", "fish"])

pr(WREN, base_targets(), "any luck fishing today?", {},
   {"name": "speak", "arguments": {"message": "No luck yet — haven't caught anything.", "emotion": "neutral"}},
   ["question", "casual_phrasing", "honest_no"])


# ---------------------------------------------------------------------------
# Trickier declines — a request that IS genuinely a request, but the honest
# in-character answer is no, for a real reason the instructions explicitly
# sanction (personality, or a physical condition), not a dodge.
# ---------------------------------------------------------------------------

pr(WREN, base_targets(travel_target_ids=["misty_mountains"]),
   "want to go check out the mountains with me?", {}, vitals=(90.0, 15.0, 70.0),
   response={"name": "speak", "arguments": {"message": "Not right now — I'm too worn out for a trip like that.", "emotion": "sad"}},
   tags=["decline", "vitals_reason"])

pr(BRAM, base_targets(nearby_npc_names=["Alex"], carried_items=["fish", "fish"]),
   "give me your fish", {"fish": 2},
   {"name": "speak", "arguments": {"message": "No. I caught these myself.", "emotion": "angry"}},
   ["decline", "personality"])

pr(MAREN, base_targets(animal_ids=["animal_0"]), "go pick a fight with that wolf for fun", {},
   {"name": "speak", "arguments": {"message": "No — that's not a fight worth picking for no reason.", "emotion": "fearful"}},
   ["decline", "reasonable_caution"],
   extra_animal_lines=["animal_0 (wolf): 150 px away — a dangerous animal, not attacking anyone right now, but worth being careful around."])

# Contrast with the above: hunting a rabbit is explicitly sanctioned by
# ActInstruction/PLAYER_REQUEST_INSTRUCTION as an ordinary thing to do —
# this should NOT be treated as an unreasonable ask to decline.
pr(BRAM, base_targets(animal_ids=["animal_1"]), "go get us some rabbit meat", {},
   {"name": "attack", "arguments": {"target_id": "animal_1", "emotion": "neutral"}},
   ["request", "attack", "hunting_not_a_decline"],
   extra_animal_lines=["animal_1 (rabbit): 80 px away — harmless, just foraging."])


# ---------------------------------------------------------------------------
# The exact real-log failure pattern: an ambient decision reacting to
# ANOTHER NPC's suggestion (not the player), where the honest, correct
# behavior is often to just act on it — not spend three more turns talking
# about it. Paired with a genuine "brush-off" case so the fine-tune doesn't
# learn "always act on anything overheard" as a shortcut either.
# ---------------------------------------------------------------------------

amb(WREN, base_targets(),
    heard="I think we should get some fish", heard_speaker="Finn", is_player_speaker=False, heard_lands=True,
    plan="Finn's right, we should actually go catch some fish instead of just talking about it.",
    inventory={},
    response={"name": "catch_fish", "arguments": {"target_id": "fish_0", "emotion": "neutral"}},
    tags=["ambient", "catch_fish", "third_party_overheard", "acting_not_just_agreeing"])

amb(MAREN, base_targets(light_fire_allowed=True),
    heard="glad you're taking care of the fire, maybe we should light some torches too?", heard_speaker="Finn",
    is_player_speaker=False, heard_lands=True,
    plan="Finn's right that the fire needs doing — I'll go light it now instead of just agreeing out loud.",
    inventory={},
    response={"name": "light_fire", "arguments": {"emotion": "neutral"}},
    tags=["ambient", "light_fire", "third_party_overheard", "acting_not_just_agreeing"])

amb(BRAM, base_targets(),
    heard="I think we should get some fish, but maybe after we work on getting the fire going?", heard_speaker="Wren",
    is_player_speaker=False, heard_lands=False,
    plan="Not my priority right now, I'd rather keep to myself.",
    inventory={},
    response={"name": "wait", "arguments": {"emotion": "neutral"}},
    tags=["ambient", "wait", "third_party_overheard", "genuine_brushoff"])

amb(FINN, base_targets(stick_ids=["stick_0"]),
    heard=None,
    plan="I noticed a stick lying around earlier, I'll grab it while I'm out here.",
    inventory={},
    response={"name": "pick_up_stick", "arguments": {"target_id": "stick_0", "emotion": "neutral"}},
    tags=["ambient", "pick_up_stick", "self_initiated"],
    extra_stick_lines=["stick_0 (stick): 110 px away, lying on the ground."])


def main():
    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with OUT_PATH.open("w") as f:
        for ex in examples:
            f.write(json.dumps(ex) + "\n")

    by_action: dict[str, int] = {}
    for ex in examples:
        by_action[ex["response"]["name"]] = by_action.get(ex["response"]["name"], 0) + 1

    print(f"wrote {len(examples)} hand-authored examples -> {OUT_PATH}")
    print("\nby chosen action:")
    for k, v in sorted(by_action.items()):
        print(f"  {k:16s} {v}")


if __name__ == "__main__":
    main()
