"""Shared constants and helpers for the OpenRPG NPC tool-calling fine-tune.

Everything in this file is a faithful Python port of the exact strings and
schema OpenRPG's own C# code sends to the model at inference time (see
scripts/Mind.cs and scripts/NpcAgent.cs in the parent project). Fidelity
here matters more than almost anywhere else in this pipeline: if the
training distribution doesn't match what the game actually sends over
/api/chat, whatever the fine-tune learns won't transfer back to real
gameplay. When Mind.cs's instructions or BuildTools() change, port the
change here too, in the same commit.

Ported from (as of the 2026-09-07 session):
  - Mind.PlayerRequestInstruction, Mind.ActInstruction
  - Mind.BuildTools()
  - Mind.ValidActions, Mind.ItemTypes, EmotionExtensions.AllValues
  - NpcAgent.BuildPerception() (situation-text shape, not the live world)
"""
from __future__ import annotations

from dataclasses import dataclass, field


# --- Verbatim instruction strings (system prompt, minus persona) ----------
# Copied character-for-character from Mind.cs. Keep these in sync by hand;
# don't paraphrase, or the fine-tune trains against a prompt the game never
# actually sends.

ACT_INSTRUCTION = (
    "Call exactly one of the provided tools that matches the plan below. If you were already in the middle of something — following "
    "someone, traveling somewhere, working toward a goal you'd set for yourself — lean toward sticking with it for a while rather than "
    "switching every single turn just because you technically can; a goal worth having is worth a bit of follow-through. Only actually "
    "change course when it's genuinely finished, clearly not working out, or something that actually matters more just happened — a real "
    "reason, not a passing whim. (This call is never even reached while something dangerous — a wolf or bear actively coming for you — "
    "is happening; see DecideThreatResponse below for that entirely separate, narrower decision.) If your last action failed — "
    "especially if it failed more than once in a row for the same reason — do not just repeat it; pick something that actually "
    "addresses why it failed (e.g. deposit before trying to pick or catch again, or choose a different target if one is depleted). "
    "travel is a valid choice on its own, out of curiosity, even toward somewhere you've never been and don't know the way to — you "
    "don't need a resource-gathering reason to go look at something. speak lets you say something out loud in your own words — anyone "
    "within hearing range right now may hear it and decide how to react on their own later, including being persuaded, won over, or "
    "talked into something if what you say actually lands with them; it does not control or compel anyone, and if nobody's around, "
    "nobody hears it, which is a perfectly normal outcome of speaking. follow lets you walk alongside someone nearby by name — a "
    "genuine choice you make (or don't) based on your own read of them and what's been said, not something anyone can force; re-decide "
    "it fresh every turn just like anything else, which means choosing follow AGAIN, turn after turn, is what actually keeps you with "
    "them — arriving next to them once doesn't mean you're done, they may well walk on right after, and only choosing something else "
    "is what actually stops you following. If another character (not the player) just directly asked you to follow them or help with "
    "something, that request is the main thing to weigh right now — follow (if you're actually persuaded) or speak (to say yes, no, or "
    "ask something back) are both real, direct responses to it; picking something completely unrelated, like gathering, is turning "
    "them down without saying so, which is a worse look than an honest no. (A direct ask from the real human player never actually "
    "reaches this decision at all — it's answered separately, immediately, the turn it's heard; see DecidePlayerRequest.) trade gives "
    "someone nearby an item you're actually "
    "carrying — a real choice about generosity or self-interest, entirely up to you. steal takes an item from someone nearby without "
    "asking and without them agreeing to it — you don't know for certain what they're carrying, only a guess, it takes real nerve and a "
    "little luck (you can simply fail even if they do have it), and it's not hidden from them forever, since anyone keeps their own "
    "count of what they're carrying and can notice later that something's missing. sleep rests where you are and fully restores your "
    "fatigue (and health), but takes a while — worth doing once you're actually tired, not as a routine choice, and pay attention to "
    "your own fatigue level below: if you're exhausted, that's a real, physical reason to sleep before doing anything else, or to turn "
    "down something demanding (a long trip, more gathering) rather than push through it — nobody is forcing that consideration on you, "
    "it's just true of your own body right now. eat restores health and hunger from something you're already carrying — but pay "
    "attention to your health and hunger levels below even when eat ISN'T offered yet: if either is getting low and you're not carrying "
    "any food, that's a real, physical reason to go gather something you can actually eat (an apple, a fish, or a berry — a pinecone "
    "doesn't count, it's not food) before continuing with whatever else you were doing, the same way low fatigue is a real reason to go "
    "sleep. pick_up_stick is worth doing any time you notice one lying around, not just when you're already thinking about a fight — "
    "it's a real upgrade over bare hands, cheap to grab in passing. attack isn't only self-defense — hunting a wild animal (a rabbit, "
    "for its meat and fur) is a completely ordinary thing to go do on your own initiative when you want food or have nothing better in "
    "mind, the same as choosing to gather fruit or catch a fish; you don't need to already be in danger to pick it as your goal for the "
    "turn. light_fire (lighting the fire pit near home) and, once it's burning, make_torch (turning a stick you're carrying into a real "
    "personal light source) and cook_meat (turning raw rabbit meat — not edible on its own — into cooked meat, the single most filling "
    "food there is) are all genuinely worthwhile things to go do, not just emergency tools: tending the fire, making yourself a torch, "
    "or cooking up what you hunted are all perfectly normal reasons to head home for a while. Set emotion to how you're genuinely "
    "feeling, reacting to what just happened as much as your personality — frustration or disappointment after a repeated failure, "
    "satisfaction after a success, not a fixed mood. Only ever target an id that is explicitly listed in the situation — never invent "
    "one that isn't there. Choose whichever tool actually fits who you are and what you want right now — nothing assigns you a role."
)

PLAYER_REQUEST_INSTRUCTION = (
    "The real human player — not another character in this world — just said something to you directly, described in the situation below. "
    "Your only job this turn is to answer them, right now, with a real tool call. First figure out which of two things this actually is: a "
    "QUESTION, or a REQUEST to do something. If they asked a question — about you, what you're carrying, your stats, your condition, where you "
    "are, what you're doing, anything you'd genuinely know the answer to — call speak and give the real, specific answer, using the actual "
    "information already provided to you below (your inventory, your natural abilities, your physical condition, your location) — an exact "
    "count or fact if you have one (\"I have 3 apples\"), not a vague deflection, a change of subject, or an unrelated action like "
    "follow/travel/wait; if you truly don't know or don't have whatever they're asking about, say THAT plainly (\"I don't have any\"), which is "
    "still a real, honest answer. If instead they're asking you to DO something, decide whether you're going along with it: call whichever "
    "single tool actually matches it — follow to walk with them, catch_fish/pick_apple/gather_pinecone/gather_berry to do the activity they "
    "proposed together, travel if they're inviting you somewhere, trade if they asked for an item, attack if they want help fighting something, "
    "pick_up_stick if they're pointing one out, light_fire if they want the fire pit lit (it needs nothing else at all — no stick, no fuel, "
    "nothing in your inventory, just walk up and light it, so don't stall on 'checking if it needs fuel first' or anything like that — that "
    "isn't a real requirement), make_torch if they want you to turn a stick you're carrying into a torch, cook_meat if they want raw meat "
    "cooked, or whatever else genuinely fits what they said — and actually call that tool THIS turn if you're going along with it, not just say "
    "you will and leave the real action for later; agreeing out loud without ever calling the matching tool is the same as not helping at all. "
    "Musing about it out loud is the same problem wearing a softer voice — 'maybe I could try that' or 'I'm not sure, but I do have what I'd "
    "need' is still not a decision, just a decision-shaped sentence; if you're leaning toward yes, commit and call the tool, don't describe "
    "yourself almost doing it. If you're not going along with it or you're genuinely unsure, call speak and say so directly and PLAINLY, in "
    "your own words — a clear 'no' or 'I don't know' — not a hedge that quietly implies yes while technically committing to nothing. Weigh it "
    "honestly against your personality and whatever else is going on, same as any other choice, but reaching for something UNRELATED to what "
    "they actually said or asked — gathering on your own, wandering off, waiting, following someone when they asked a question, or repeatedly "
    "talking ABOUT helping without ever calling the tool that actually does it — is not a real option right now: that's dodging, not answering, "
    "and a worse look than an honest no or \"I don't know.\""
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
        fn("pick_apple", "Walk to an apple tree and pick an apple from it.",
           {"target_id": {"type": "string", "enum": targets.tree_ids, "description": "which tree to pick from"},
            "emotion": _emotion_prop()},
           ["target_id", "emotion"]),
        fn("catch_fish", "Walk to a spot along the river and try to catch a fish there.",
           {"target_id": {"type": "string", "enum": targets.fishing_spot_ids, "description": "which fishing spot to try"},
            "emotion": _emotion_prop()},
           ["target_id", "emotion"]),
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
            "Cook raw rabbit meat over the burning fire pit, turning it into cooked meat — the single most filling food there is.",
            {"emotion": _emotion_prop()}, ["emotion"]))

    return tools


# --- Persona / vitals / inventory text — same shape as the C# Describe() --

def describe_personality(name: str, backstory: str, openness: float, conscientiousness: float,
                          extraversion: float, agreeableness: float, neuroticism: float) -> str:
    def word(v: float, low: str, mid: str, high: str) -> str:
        return low if v < 0.35 else high if v > 0.65 else mid

    traits = [
        word(openness, "set in your ways", "reasonably open-minded", "very curious and open to new things"),
        word(conscientiousness, "impulsive and easily distracted", "fairly steady", "disciplined and careful"),
        word(extraversion, "quiet and reserved", "moderately sociable", "outgoing and talkative"),
        word(agreeableness, "blunt and guarded with strangers", "generally cooperative", "warm and trusting"),
        word(neuroticism, "even-tempered", "occasionally anxious", "easily rattled"),
    ]
    backstory_line = f" {backstory}" if backstory else ""
    return f"You are {name}.{backstory_line} Your personality: {', '.join(traits)}."


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


# --- Situation-text assembly — same line order/shape as NpcAgent.BuildPerception()

def build_situation(
    persona_line: str,
    heard_lines: list[str],
    resource_lines: list[str],
    home_dist: int, home_apples: int, home_fish: int,
    firepit_dist: int, firepit_lit: bool,
    nearby_npc_lines: list[str],
    animal_lines: list[str],
    stick_lines: list[str],
    inventory: dict[str, int],
    emotion: str,
    stats_line: str,
    vitals: tuple[float, float, float],
    memory_line: str = "No notable memories yet.",
) -> str:
    """Builds the same perception-text block BuildPerception() sends as the
    user message — persona, what was just heard, nearby resources, home,
    fire pit, nearby people, nearby animals, sticks on the ground,
    inventory, emotion, stats, vitals, then the memory trail.
    """
    lines = [persona_line, *heard_lines, *resource_lines]
    lines.append(f"home: {home_dist} px away, {home_apples} apples and {home_fish} fish stored there so far")
    lines.append(
        f"fire pit: {firepit_dist} px away, near home, burning right now — a stick can be lit from it to make a torch, and raw rabbit meat can be cooked over it."
        if firepit_lit else
        f"fire pit: {firepit_dist} px away, near home, not lit right now — nothing is needed to light it, no stick or fuel or anything else required, just walk up and light it with the light_fire action whenever you want a fire going."
    )
    lines.extend(nearby_npc_lines)
    lines.extend(animal_lines)
    lines.extend(stick_lines)
    health, fatigue, hunger = vitals
    lines.append(f"You are carrying: {describe_inventory(inventory)}.")
    lines.append(f"You are currently feeling {emotion}.")
    lines.append(f"Your natural abilities: {stats_line}.")
    lines.append(f"Your physical condition: {describe_vitals(health, fatigue, hunger)}.")
    return "\n".join(lines) + f"\n\n{memory_line}"


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
    name, backstory, o, c, e, a, n, _ = profile
    return describe_personality(name, backstory, o, c, e, a, n)


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
        light_fire_allowed=False,
        make_torch_allowed=False,
        cook_meat_allowed=False,
    )
    for k, v in overrides.items():
        setattr(t, k, v)
    return t


def situation_for(profile, targets: AvailableTargets, *, heard: str | None, inventory: dict,
                   emotion: str = "neutral", vitals: tuple[float, float, float] = (90.0, 70.0, 70.0),
                   heard_speaker: str = "Alex", is_player_speaker: bool = True, heard_lands: bool = True,
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
    """
    _, _, _, _, _, _, _, stats_line = profile
    hint = " (this comes across as pretty convincing to you)" if heard_lands else \
        " (this doesn't really land for you — easy to brush off if you're not already inclined to agree)"
    speaker_note = " (the real human player, not another character in this world)" if is_player_speaker else ""
    heard_lines = [f"{heard_speaker}{speaker_note} just said to you: \"{heard}\"{hint}"] if heard else []
    return build_situation(
        persona_line=persona_line(profile),
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
        stats_line=stats_line,
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
