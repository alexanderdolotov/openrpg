"""Shared constants and helpers for the OpenRPG NPC tool-calling fine-tune.

Everything in this file is a faithful Python port of the exact strings and
schema OpenRPG's own C# code sends to the model at inference time (see
scripts/Mind.cs and scripts/NpcAgent.cs in the parent project). Fidelity
here matters more than almost anywhere else in this pipeline: if the
training distribution doesn't match what the game actually sends over
/api/chat, whatever the fine-tune learns won't transfer back to real
gameplay. When Mind.cs's instructions or BuildTools() change, port the
change here too, in the same commit.

Ported from (as of the 2026-09-08 clean-prompt rewrite, ported into the
real game the same day):
  - Mind.PlayerRequestInstruction, Mind.ActInstruction
  - Mind.BuildTools()
  - Mind.ValidActions, Mind.ItemTypes, EmotionExtensions.AllValues
  - Personality.DescribeForPrompt() (BACKGROUND/PERSONALITY; STATS is
    appended by Mind.Decide/DecidePlayerRequest, not Personality itself)
  - NpcAgent.BuildPerception() (situation-text shape, not the live world)
"""
from __future__ import annotations

from dataclasses import dataclass, field


# --- Verbatim instruction strings (system prompt, minus persona) ----------
# Copied character-for-character from Mind.cs. Keep these in sync by hand;
# don't paraphrase, or the fine-tune trains against a prompt the game never
# actually sends.

# --- Rewritten clean/minimal, 2026-09-08, per the hypothesis that the old
# paragraph-of-nuance instructions (preserved via git history) were adding
# noise a 3B model struggles to track rather than helping it —
# llm_tuning's baseline eval measured no real regression from shrinking
# them, and PORTED INTO Mind.cs/NpcAgent.cs/Personality.cs the same day.
# This file and the real game's prompt are meant to stay in sync from here
# — when one changes, port the change to the other in the same commit,
# same fidelity rule this file has always followed. See build_situation()/
# persona_line() below for the matching switch to labeled sections in the
# situation text itself.
ACT_INSTRUCTION = (
    "Call exactly one of the tools listed below — whichever one best fits your personality, stats, and the situation above right now. "
    "If your last action just failed, don't repeat it — pick something that addresses why. Only target an id that's explicitly listed "
    "above; never invent one."
)

PLAYER_REQUEST_INSTRUCTION = (
    "The player just spoke to you directly — see HEARD above. Answer them this turn with a real tool call, not just words. "
    "If it's a question, call speak with the honest, specific answer from what's listed above. If it's a request to do something, call "
    "the one matching tool right now if you're willing — including a casual \"come with me\"/\"walk with me\"/\"stay with me\" (that's "
    "follow, even without the word \"follow\" in it) — or call speak to say no, plainly, if you're not willing, or if the tool for it "
    "isn't listed below at all right now. If they said both a question AND a request in the same line, answer the request — it's "
    "the time-sensitive half; the fact they asked about is still just as true and still answerable next time they ask."
)

EMOTIONS = ["neutral", "happy", "sad", "excited", "fearful", "angry", "curious", "content"]

ITEM_TYPES = [
    "apple", "fish", "pinecone", "blueberry", "blackberry", "raspberry",
    "stick", "rabbit_meat", "fur", "torch", "cooked_meat",
]

VALID_ACTIONS = [
    "pick_apple", "catch_fish", "gather_pinecone", "gather_berry", "deposit", "travel",
    "speak", "follow", "trade", "steal", "attack", "eat", "pick_up_stick", "sleep",
    "wait", "light_fire", "make_torch", "cook_meat",
]


# --- AvailableTargets: same fields as Mind.AvailableTargets ---------------

@dataclass
class AvailableTargets:
    tree_ids: list[str] = field(default_factory=list)
    fishing_spot_ids: list[str] = field(default_factory=list)
    pine_tree_ids: list[str] = field(default_factory=list)
    berry_bush_ids: list[str] = field(default_factory=list)
    travel_target_ids: list[str] = field(default_factory=list)
    nearby_npc_names: list[str] = field(default_factory=list)
    carried_items: list[str] = field(default_factory=list)
    sleep_allowed: bool = False
    animal_ids: list[str] = field(default_factory=list)
    stick_ids: list[str] = field(default_factory=list)
    eat_allowed: bool = False
    light_fire_allowed: bool = False
    make_torch_allowed: bool = False
    cook_meat_allowed: bool = False


def _emotion_prop(description: str = "how you're feeling right now") -> dict:
    return {"type": "string", "enum": EMOTIONS, "description": description}



def build_tools(targets: AvailableTargets) -> list[dict]:
    """Faithful port of Mind.BuildTools() — same tools, same conditional
    gating, same field names, in the same order. This is exactly the
    `tools` array the game sends over /api/chat; training examples must be
    built against tool lists shaped like this one, not a hand-simplified
    version, or the model learns a schema Ollama never actually offers it.
    """
    def fn(name: str, description: str, properties: dict, required: list[str]) -> dict:
        return {
            "type": "function",
            "function": {
                "name": name,
                "description": description,
                "parameters": {"type": "object", "properties": properties, "required": required},
            },
        }

    tools = [
        fn("deposit", "Walk home and deposit everything you're currently carrying, whatever the mix of items.",
           {"target_id": {"type": "string", "enum": ["home"]}, "emotion": _emotion_prop()},
           ["target_id", "emotion"]),
        fn("wait", "Do nothing this turn.",
           {"emotion": _emotion_prop()},
           ["emotion"]),
        fn("speak",
           "Say something out loud, in your own words — a greeting, a story, an argument, a pitch trying to talk someone into "
           "something, anything. Anyone within hearing range right now may hear it and decide how to react on their own, later, "
           "including being persuaded if what you say actually lands with them — this does not control or compel anyone, and if "
           "nobody's around, nobody hears it.",
           {"message": {"type": "string", "description": "what you say out loud"}, "emotion": _emotion_prop()},
           ["message", "emotion"]),
    ]

    if targets.sleep_allowed:
        tools.append(fn("sleep",
            "Rest right where you are and fully restore your fatigue. Takes a while — a real choice for when you're actually tired, not routine.",
            {"emotion": _emotion_prop()}, ["emotion"]))

    # pick_apple/catch_fish used to sit unconditionally above — "hand-placed
    # and guaranteed to exist from the start." That covered EXISTENCE, not
    # PROXIMITY: tree_ids/fishing_spot_ids are vision-filtered now (see
    # NpcAgent.SortByDistance's own header in the parent project), so the
    # enum genuinely can be empty. Same enum-of-nothing guard every other
    # resource tool already needed.
    if targets.tree_ids:
        tools.append(fn("pick_apple", "Walk to an apple tree and pick an apple from it.",
            {"target_id": {"type": "string", "enum": targets.tree_ids, "description": "which tree to pick from"},
             "emotion": _emotion_prop()}, ["target_id", "emotion"]))

    if targets.fishing_spot_ids:
        tools.append(fn("catch_fish", "Walk to a spot along the river and try to catch a fish there.",
            {"target_id": {"type": "string", "enum": targets.fishing_spot_ids, "description": "which fishing spot to try"},
             "emotion": _emotion_prop()}, ["target_id", "emotion"]))

    if targets.pine_tree_ids:
        tools.append(fn("gather_pinecone", "Walk to a pine tree and gather a pinecone from it.",
            {"target_id": {"type": "string", "enum": targets.pine_tree_ids, "description": "which pine tree to gather from"},
             "emotion": _emotion_prop()}, ["target_id", "emotion"]))

    if targets.berry_bush_ids:
        tools.append(fn("gather_berry", "Walk to a berry bush and pick a berry from it — whichever kind that particular bush actually grows.",
            {"target_id": {"type": "string", "enum": targets.berry_bush_ids, "description": "which berry bush to gather from"},
             "emotion": _emotion_prop()}, ["target_id", "emotion"]))

    if targets.travel_target_ids:
        tools.append(fn("travel", "Walk toward a distant landmark out of curiosity, not to gather anything — you may not know the way there yet.",
            {"target_id": {"type": "string", "enum": targets.travel_target_ids, "description": "which distant landmark to head toward"},
             "emotion": _emotion_prop()}, ["target_id", "emotion"]))

    if targets.nearby_npc_names:
        tools.append(fn("follow", "Walk alongside someone nearby, by name — your own choice, re-decided fresh every turn, not a commitment.",
            {"target_id": {"type": "string", "enum": targets.nearby_npc_names, "description": "the name of who to follow"},
             "emotion": _emotion_prop()}, ["target_id", "emotion"]))

    if targets.nearby_npc_names and targets.carried_items:
        tools.append(fn("trade", "Give someone nearby an item you're carrying — a real choice about generosity or self-interest.",
            {"target_id": {"type": "string", "enum": targets.nearby_npc_names, "description": "who to give it to"},
             "item": {"type": "string", "enum": targets.carried_items, "description": "which item to give — only what you're actually carrying"},
             "amount": {"type": "integer", "description": "how many to give (defaults to 1)"},
             "emotion": _emotion_prop()}, ["target_id", "item", "emotion"]))

    if targets.nearby_npc_names:
        tools.append(fn("steal",
            "Take an item from someone nearby without asking. You don't know for certain what they're carrying — it's a guess, and it fails "
            "harmlessly if they don't have it. Not hidden from them forever: they keep their own count and may notice something's missing later.",
            {"target_id": {"type": "string", "enum": targets.nearby_npc_names, "description": "who to take it from"},
             "item": {"type": "string", "enum": ITEM_TYPES, "description": "which item to try to take"},
             "amount": {"type": "integer", "description": "how many to try to take (defaults to 1)"},
             "emotion": _emotion_prop()}, ["target_id", "item", "emotion"]))

    if targets.animal_ids:
        tools.append(fn("attack",
            "Fight a nearby wild animal — walk up and strike it, with whatever weapon (or bare hands) you're carrying. Can miss, and it can hit back.",
            {"target_id": {"type": "string", "enum": targets.animal_ids, "description": "which animal to attack"},
             "emotion": _emotion_prop()}, ["target_id", "emotion"]))

    if targets.eat_allowed:
        tools.append(fn("eat", "Eat something from what you're carrying to restore some Health. Uses whichever food you have.",
            {"emotion": _emotion_prop()}, ["emotion"]))

    if targets.stick_ids:
        tools.append(fn("pick_up_stick", "Pick up a stick lying on the ground — a basic weapon, better than bare hands in a fight.",
            {"target_id": {"type": "string", "enum": targets.stick_ids, "description": "which stick to pick up"},
             "emotion": _emotion_prop()}, ["target_id", "emotion"]))

    if targets.light_fire_allowed:
        tools.append(fn("light_fire", "Walk to the fire pit near home and light it. Stays lit for a while, then goes out on its own.",
            {"emotion": _emotion_prop()}, ["emotion"]))

    if targets.make_torch_allowed:
        tools.append(fn("make_torch",
            "Light a stick you're carrying from the burning fire pit, turning it into a torch — a personal light source. Burns out and turns "
            "back into a plain stick after a while; needs the fire pit lit again to relight.",
            {"emotion": _emotion_prop()}, ["emotion"]))

    if targets.cook_meat_allowed:
        tools.append(fn("cook_meat",
            "Cook raw rabbit meat you're carrying over the burning fire pit, turning it into cooked meat — restores far more health "
            "and hunger than raw meat (which isn't edible at all) when you eat it later, anytime, anywhere.",
            {"emotion": _emotion_prop()}, ["emotion"]))

    return tools


# --- Persona / vitals / inventory text ---------------------------------
# Labeled-section rewrite, 2026-09-08 — see ACT_INSTRUCTION's own header.
# describe_personality() used to return ONE dense sentence
# ("You are X. backstory Your personality: traits.") that was also
# duplicated as the first line of the user message (BuildPerception()'s own
# perception text starts with Personality.DescribeForPrompt() too) — this
# drops that duplication entirely: background/personality/stats now live
# ONCE, in the system message only (see persona_line() below), and the user
# message (build_situation()) covers only what changes turn to turn.

def describe_personality_traits(openness: float, conscientiousness: float,
                                 extraversion: float, agreeableness: float, neuroticism: float) -> str:
    def word(v: float, low: str, mid: str, high: str) -> str:
        return low if v < 0.35 else high if v > 0.65 else mid

    return ", ".join([
        word(openness, "set in your ways", "reasonably open-minded", "very curious and open to new things"),
        word(conscientiousness, "impulsive and easily distracted", "fairly steady", "disciplined and careful"),
        word(extraversion, "quiet and reserved", "moderately sociable", "outgoing and talkative"),
        word(agreeableness, "blunt and guarded with strangers", "generally cooperative", "warm and trusting"),
        word(neuroticism, "even-tempered", "occasionally anxious", "easily rattled"),
    ])


# Fixed, one-line scene-setting — never appeared as its own concept in the
# old prompt (the dynamic resource lines WERE the only sense of place a
# turn got). Static per the whole project's world (see the parent repo's
# README's "Apple Garden Prototype"), so a single constant is enough.
SETTING_LINE = (
    "A garden clearing by your home, beside a winding river, with forest, foothills, and misty mountains to the north."
)


def describe_inventory(counts: dict[str, int]) -> str:
    if not counts:
        return "nothing"
    parts = [f"{v} {k}" + ("s" if v != 1 else "") for k, v in counts.items()]
    return ", ".join(parts)


def describe_vitals(health: float, fatigue: float, hunger: float) -> str:
    fatigue_state = ("completely exhausted" if fatigue <= 0 else
                      "exhausted — needs sleep soon" if fatigue < 30 else
                      "getting tired" if fatigue < 60 else "well-rested")
    hunger_state = ("starving — losing health" if hunger <= 15 else
                     "hungry — should eat soon" if hunger < 40 else
                     "getting hungry" if hunger < 65 else "well-fed")
    return f"health {health:.0f}/100, fatigue {fatigue:.0f}/100 ({fatigue_state}), hunger {hunger:.0f}/100 ({hunger_state})"


# --- Situation-text assembly ---------------------------------------------
# Labeled sections, 2026-09-08, replacing the old flat list
# of unlabeled lines (persona repeated, then heard/resources/home/firepit/
# nearby/inventory/emotion/stats/vitals one after another with nothing
# marking where one kind of information ends and the next begins). Same
# underlying facts, same values — this only changes how they're grouped and
# labeled, on the hypothesis that a 3B model tracks "ENVIRONMENT: ... /
# NEARBY: ... / HEARD: ..." more reliably than an undifferentiated
# paragraph of one-off sentences.

def build_situation(
    heard_lines: list[str],
    resource_lines: list[str],
    home_dist: int, home_apples: int, home_fish: int,
    firepit_dist: int, firepit_lit: bool,
    nearby_npc_lines: list[str],
    animal_lines: list[str],
    stick_lines: list[str],
    inventory: dict[str, int],
    emotion: str,
    vitals: tuple[float, float, float],
    memory_line: str = "No notable memories yet.",
) -> str:
    """User-message content: SETTING, ENVIRONMENT (resources/home/fire pit),
    NEARBY (people/animals/sticks), MEMORY, YOU (inventory/emotion/vitals),
    HEARD — in that order. Background/personality/stats live in the system
    message instead (see persona_line()), not duplicated here. HEARD, not
    PLAYER SAID — situation_for's own heard_speaker/is_player_speaker let
    this same slot carry an overheard THIRD PARTY's line (the ambient
    third-party-overheard scenarios below), where "player said" would be
    flatly false.
    """
    sections = [f"SETTING: {SETTING_LINE}"]

    # Resource lines (trees/fish/pine/berry) sit under ENVIRONMENT, NOT
    # merged into NEARBY's budgeted people/animal/stick pool — reverted
    # 2026-09-08, the same day as the ENVIRONMENT/NEARBY split itself: an
    # earlier version of this function DID put them under NEARBY (matching
    # NpcAgent.BuildPerception()'s own PRE-EXISTING merge, which shared one
    # budget across all seven categories), and a live A/B run of that
    # exact change measured pick_apple recall crash from a consistent
    # 66-83% (four separate prior runs) to 8.3% — a real, reproducible
    # regression, not sampling noise. Ported the OTHER direction instead:
    # NpcAgent.BuildPerception() itself changed to match THIS shape (see
    # its own comment), a deliberate real-game behavior change motivated
    # by this measurement, not a training-scaffold shortcut.
    env_lines = [*resource_lines]
    env_lines.append(f"home: {home_dist} px away, {home_apples} apples and {home_fish} fish stored there so far")
    env_lines.append(
        f"fire pit: {firepit_dist} px away, near home, burning right now — a stick can be lit from it to make a torch, and raw rabbit meat can be cooked over it."
        if firepit_lit else
        f"fire pit: {firepit_dist} px away, near home, not lit right now — nothing is needed to light it, no stick or fuel or anything else required, just walk up and light it with the light_fire action whenever you want a fire going."
    )
    sections.append("ENVIRONMENT:\n" + "\n".join(env_lines))

    nearby_lines = [*nearby_npc_lines, *animal_lines, *stick_lines]
    if nearby_lines:
        sections.append("NEARBY:\n" + "\n".join(nearby_lines))

    sections.append(f"MEMORY: {memory_line}")

    health, fatigue, hunger = vitals
    sections.append(
        f"YOU: carrying {describe_inventory(inventory)}; feeling {emotion}; {describe_vitals(health, fatigue, hunger)}"
    )

    if heard_lines:
        sections.append("HEARD:\n" + "\n".join(heard_lines))

    return "\n\n".join(sections)


# --- Training example shape ------------------------------------------------
# Deliberately backend-agnostic (plain system/user/tools/response dict), the
# same separation their finance-tuning repo uses (to_chat_dataset() there
# bridges labeled parquet rows -> whatever chat template a given base model
# needs). Actual chat-template rendering for a specific base model belongs
# in the fine-tune script, not here — that's a training detail, not a fact
# about what the game sends.

def make_example(mode: str, system: str, user: str, tools: list[dict], response: dict,
                  tags: list[str] | None = None) -> dict:
    """response: {"name": <tool name>, "arguments": {...}} — the ideal
    tool call for this situation, in the same shape ParseToolCall() reads
    back out of a real Ollama response.
    """
    return {"mode": mode, "system": system, "user": user, "tools": tools, "response": response, "tags": tags or []}


# --- Reusable NPC profiles + scenario scaffolding --------------------------
# Shared by every generation stage (1_generate_scenarios.py, 2_..., ...) so
# "what a typical situation looks like" is defined once. A numbered script
# can't be imported by name (`import 1_generate_scenarios` is a syntax
# error), so this lives here rather than in stage 1.

# (name, backstory, O, C, E, A, N, stats_line) — same four archetypes as the
# real npcs.json roster, plus Bram for a genuinely low-agreeableness profile
# the real roster doesn't currently have (needed for believable declines).
PROFILES = [
    ("Maren", "A blacksmith's apprentice who comes to this garden looking for a quiet place to think.",
     0.60, 0.70, 0.30, 0.50, 0.40, "STR 13 DEX 11 CON 11 INT 9 WIS 10 CHA 8 BRV 8"),
    ("Finn", "Grew up on a fishing boat and feels most himself with his feet in the water.",
     0.70, 0.40, 0.60, 0.60, 0.30, "STR 13 DEX 12 CON 7 INT 7 WIS 7 CHA 10 BRV 10"),
    ("Wren", "Has never stayed anywhere long. Every hazy shape on the horizon is an open question.",
     0.90, 0.25, 0.55, 0.50, 0.20, "STR 10 DEX 11 CON 10 INT 11 WIS 11 CHA 17 BRV 10"),
    ("Bram", "A blunt, guarded trapper who trusts very few people and even fewer plans.",
     0.30, 0.55, 0.25, 0.15, 0.55, "STR 15 DEX 13 CON 14 INT 8 WIS 9 CHA 6 BRV 12"),
]


def persona_line(profile) -> str:
    """System-message content: BACKGROUND, PERSONALITY, STATS — the parts of
    a character that don't change turn to turn, each its own labeled line
    (see build_situation()'s own header for why this and the per-turn
    situation text no longer duplicate persona between them).
    """
    name, backstory, o, c, e, a, n, stats_line = profile
    background = f"You are {name}." + (f" {backstory}" if backstory else "")
    return (
        f"BACKGROUND: {background}\n"
        f"PERSONALITY: {describe_personality_traits(o, c, e, a, n)}\n"
        f"STATS: {stats_line}"
    )


def base_targets(**overrides) -> AvailableTargets:
    t = AvailableTargets(
        tree_ids=["tree_0", "tree_1"],
        fishing_spot_ids=["fish_0"],
        pine_tree_ids=["pine_0"],
        berry_bush_ids=["berry_0"],
        travel_target_ids=["misty_mountains"],
        nearby_npc_names=["Alex"],  # the player, when standing nearby — same as any other agent name
        carried_items=[],
        sleep_allowed=False,
        animal_ids=[],
        stick_ids=[],
        eat_allowed=False,
        # True by default — matches the real invariant (NpcAgent:
        # LightFireAllowed = !FirePit.IsLit) and situation_for's own
        # default firepit_lit=False (make_torch_allowed/cook_meat_allowed
        # both default False too): an unlit fire pit ALWAYS allows
        # light_fire, no other gate on it. False was the default here
        # until 2026-09-08 — every scenario that didn't explicitly need
        # light_fire offered was quietly telling the model "light it
        # whenever you want with light_fire" in the situation text while
        # never actually putting light_fire in its tools list. Scenarios
        # that want a LIT fire pit instead (make_torch_allowed=True or
        # cook_meat_allowed=True) now need light_fire_allowed=False
        # alongside that, explicitly — see those call sites.
        light_fire_allowed=True,
        make_torch_allowed=False,
        cook_meat_allowed=False,
    )
    for k, v in overrides.items():
        setattr(t, k, v)
    return t


def situation_for(profile, targets: AvailableTargets, *, heard: str | None, inventory: dict,
                   emotion: str = "neutral", vitals: tuple[float, float, float] = (90.0, 70.0, 70.0),
                   heard_speaker: str = "Alex", is_player_speaker: bool = True, heard_lands: bool = True,
                   witnessed: str | None = None,
                   extra_resource_lines: list[str] | None = None,
                   extra_stick_lines: list[str] | None = None, extra_animal_lines: list[str] | None = None,
                   extra_npc_lines: list[str] | None = None) -> str:
    """Exact format of NpcAgent's own freshHeard lines (see BuildPerception's
    caller, the foreach over SpeechLog.Overheard): "{speaker}{note} just
    said to you: \"{message}\"{hint}", where note marks the real player
    specifically and hint is the persuasion-landed/didn't-land tag.
    heard_speaker/is_player_speaker/heard_lands let a scenario be "overheard
    from another NPC, and it doesn't land" (the real ambient failure from
    the logs — Maren hearing Finn's fire suggestion and brushing it off) as
    well as the direct-player-request case.

    witnessed: a non-speech event this NPC saw (WorldEventLog.Witnessed,
    e.g. "Finn picked an apple from tree_0") — merged into the same HEARD
    slot as heard speech, "You just saw: {witnessed}" exactly matching
    NpcAgent's own freshWitnessed line, since real BuildPerception() now
    puts both under one HEARD section (see build_situation()'s own
    header). Independent of `heard`: a turn can have either, both, or
    neither.
    """
    hint = " (this comes across as pretty convincing to you)" if heard_lands else \
        " (this doesn't really land for you — easy to brush off if you're not already inclined to agree)"
    speaker_note = " (the real human player, not another character in this world)" if is_player_speaker else ""
    heard_lines = [f"{heard_speaker}{speaker_note} just said to you: \"{heard}\"{hint}"] if heard else []
    if witnessed:
        heard_lines.append(f"You just saw: {witnessed}")
    return build_situation(
        heard_lines=heard_lines,
        resource_lines=extra_resource_lines if extra_resource_lines is not None else [
            "tree_0 (apple tree): 140 px away, 3 apples ready to pick.",
            "berry_0 (berry bush): 260 px away, 2 berries ready to pick.",
        ],
        home_dist=220, home_apples=4, home_fish=1,
        firepit_dist=200, firepit_lit=targets.make_torch_allowed or targets.cook_meat_allowed,
        nearby_npc_lines=(extra_npc_lines if extra_npc_lines is not None else ["Alex is nearby, 90 px away, feeling neutral."]),
        animal_lines=extra_animal_lines or [],
        stick_lines=extra_stick_lines or [],
        inventory=inventory,
        emotion=emotion,
        vitals=vitals,
    )


# --- Model registry (same convention as the finance-tuning repo's MODEL_MAP)

MODEL_MAP = {
    "llama3.2:3b": {
        "hf_checkpoint": "unsloth/Llama-3.2-3B-Instruct",
        "ollama_base_tag": "llama3.2:3b",
        "ollama_tuned_tag": "openrpg-npc-llama3.2-3b",
    },
}
