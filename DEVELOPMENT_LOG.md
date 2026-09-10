# Development Log

This is the detailed design/implementation record for the project — the
"why," not just the "what." [README.md](README.md) is the short version
for getting the game running; this file is for understanding or changing
how it actually works.

It reads roughly chronologically, oldest decisions first, and is added
to as the game grows rather than rewritten to stay "current" — so later
sections (wildlife, combat, day/night, fire pits — see the git log for
the full picture) may reference systems only described briefly, or not
yet written up here at all. When in doubt, the code and its comments are
the source of truth; this file explains the reasoning behind it.

## Origins: proving the mind/engine split

The project started as a bare-bones Godot 4 scene with no art — a
brown-and-triangle-roofed home, three green circles (apple trees), a
blue river with two fishing spots, misty mountains and a forest/
foothills/river-crossing obstacle course to the north, three colored
squares (Maren, Finn, Wren — LLM-driven) and one more controlled by the
player — built to prove out one thing before anything else got touched:
the loop between a "mind" deciding *what* to do and an engine actually
carrying it out, including what happens when an attempt fails.

Every character — NPC or player — shares the same action set (gather,
deposit, travel, speak, follow, trade, steal, persuade, sleep, wait, and
later attack), the same `Inventory`, the same rolled `CharacterStats`
(D&D-style STR/DEX/CON/INT/WIS/CHA) and `Vitals` (health/fatigue), and
the same dice-backed `SkillCheck` resolving anything that isn't a sure
thing. The *only* difference between an NPC and the player is what
decides an action — `Mind.Decide()` for one, keyboard/mouse for the
other. See "The player character" and "Inventory, stats, and vitals"
below.

Written in **C#**, not GDScript — ported over once the core loop was
proven, since GDScript's dynamic typing had already caused two real bugs
(a method-resolution issue, a runtime type crash on malformed LLM
output) that C#'s compiler catches before you ever hit Play.

## What's actually being tested

`scripts/Mind.cs` builds the prompts and validates whatever comes back,
completely independent of who's answering; `scripts/OllamaProvider.cs`
and `scripts/OpenAiCompatibleProvider.cs` are the two swappable
backends, chosen by `MindConfig` at startup. Whichever one is active,
each NPC's tools are defined the same way and the world's live ids
(trees, fishing spots, etc.) are built into the schema each call so the
model can't target one that doesn't exist. The "think" call's `content`
is logged as the NPC's thought; the "act" call's `tool_calls[0]`, once
validated, becomes the next `GameAction`.

If anything fails — the backend unreachable, a timeout, an unrecognized
action name, a target that isn't currently real — `NpcAgent`'s
`RandomFallback()` picks the next action instead: hands full? deposit.
Otherwise, a random pick among whatever's actually available (an apple
tree with fruit left, a fishing spot with fish left). The log says which
path produced each action.

Everything else is the permanent part:

- **`GameAction`** — what the mind hands down: an action id and a
  target id (never coordinates). `pick_apple` targeting `tree_1`, not
  "move to (620, 220) then pick."
- **`NPCActor`** — the tactical executor. Given a `GameAction` it walks
  toward the target, checks range every physics frame, retries the
  approach on its own, and only reports back (`ActionCompleted`) once
  the action is truly resolved. The mind is never asked "should I move
  closer?" — that's handled silently, every frame, by the executor.
- **`IInteractable`** (`AppleTree` / `Home`, etc.) — each target owns
  its own rules via `TryInteract()`. In the GDScript version this was
  duck-typed (`has_method("try_interact")`, checked at runtime); in C#
  it's a real interface, checked at compile time.
- **`WorldRegistry`** — the only vocabulary a mind is allowed to
  reference targets by. If an id isn't registered, `target_not_found`
  comes back as a normal, handleable failure instead of a crash.
- **`Mind`'s validation layer** — every field coming back from whichever
  provider answered is checked before it becomes a `GameAction`:
  unrecognized action names, invalid targets, and the
  `["tree_0"]`-instead-of-`"tree_0"` quirk small models produce are all
  caught explicitly rather than assumed away. Every stage returns an
  explicit ok/error result the caller must check, rather than leaning on
  exceptions.
- **`ILlmProvider`** — the seam between "decide what to do" and "which
  API answered." `OllamaProvider` and `OpenAiCompatibleProvider` adapt
  their own wire formats (Ollama's `arguments` come back as a real JSON
  object; OpenAI-compatible APIs encode it as a JSON *string* that needs
  a second parse) into the same shape, so `Mind` never has to know or
  care which one is active.

## Logging to a file

Set `"log_npc_thoughts": true` in `mind.local.json` to also write every
thought, emotion, action attempt/result, fallback, and memory
compression to a plain-text file (gitignored `logs/` directory, created
automatically) — off by default. Each run gets its own file,
`logs/npc_thoughts_<started-at>.log`, rather than every session
appending to one growing file, so two runs are easy to diff or compare
side by side; the startup line in the console names the exact file for
that run. Useful for reviewing a whole play session afterward instead of
scrolling the in-game log.

## No hardcoded roles

NPCs are built through `NpcFactory.Create()`, which assembles the full
bundle — `NPCActor`, a `Mind` with its own `ILlmProvider` instance
(never shared between agents; one `HttpRequest` node can't have two
requests in flight at once), an `NpcMemory`, and a `Personality` — into
one `NpcAgent`. Adding an NPC is one factory call against a
`WorldContext` (the shared trees/fishing spots/home), not a rewrite.

Critically: `Mind.BuildTools()` hands every NPC the *identical* tool
menu. Nothing in `NpcAgent`, `NpcFactory`, or `Mind` branches on which
NPC plays which role. The only thing that differs between NPCs is the
text their `Personality.DescribeForPrompt()` puts in the system prompt
— e.g. Finn's backstory leans toward water, Maren's doesn't — and
whatever role that produces is the model's own inference from that
framing each turn, not a code path. Watch the logs; there's no guarantee
any NPC picks the role you'd expect, which is the point.

`Personality` (`Personality.cs`) also does two other jobs: its five
traits (openness, conscientiousness, extraversion, agreeableness,
neuroticism, each 0-1) get worded into the persona block Word-by-value
(e.g. openness 0.7 → "very curious and open to new things"), and they
derive a sampling `Temperature` — steadier/conscientious NPCs sample
cold, open/neurotic ones sample hot — wiring up the personality-driven-
temperature idea the architecture always intended.

### Editing the roster without touching code

Every NPC's id, name, backstory, start position, and visual color live
in **`npcs.json`** (checked into git — this is game content, not a
secret like `mind.local.json`). `Main.cs` just loops over whatever
`NpcRoster.Load()` returns and calls `NpcFactory.Create()` per entry —
adding a new NPC is an edit to that file, not a recompile. Missing or
malformed `npcs.json` falls back to Maren and Finn's baked-in values
(`NpcRoster.Defaults`), so the game never ends up with zero NPCs.

Personality traits themselves aren't spelled out per NPC, though —
**`npcs.json`** references a named shape from
**`personality_archetypes.json`** (via the `archetype` field) instead.
Each archetype is a reusable trait set with a human-readable
`description` (not sent to the LLM — only the numbers matter there) —
`steady_thinker`, `easygoing_wanderer`, `cautious_homebody`,
`bold_adventurer`, `gruff_loner`, `warm_socializer`, `anxious_worrier`,
`calm_pragmatist`, defined in `ArchetypeLibrary.cs`'s built-in fallback
and mirrored in the checked-in file. An unknown or misspelled archetype
id falls back to `calm_pragmatist` rather than crashing NPC creation.
Adding a new NPC that reuses an existing shape is now just
`{"archetype": "gruff_loner", ...}`; adding an entirely new shape is one
more object in `personality_archetypes.json`.

(This is JSON, not YAML — kept consistent with `npcs.json` and
`mind.local.json`, and avoids adding a third-party YAML dependency for a
handful of files.)

## The world: garden + river

`River` is purely visual (`_Draw()`, no interaction). `FishingSpot` is
`AppleTree`'s sibling — same `IInteractable` shape, `catch_fish` instead
of `pick_apple`, its own depleted-color feedback. `NPCActor.Holding` is
a string (`""` / `"apple"` / `"fish"` / …) rather than a per-resource
bool, so the tool schema doesn't grow linearly with every new resource
type added later.

`River` is a meandering ribbon, not a rectangle — a sine-wave centerline
(`Length`, `Amplitude`, `Waves`) with banks (`Thickness`) computed
perpendicular to the curve's tangent at each sample point, so it
actually reads as winding rather than a wobbly bar. `FishingSpot`
placement calls `river.GetCenterlineWorldY(x)` to sit exactly on the
curve, rather than guessing a fixed y that would land on the bank at
some points along the wave.

### The camera fits itself to the world

Every world object with a real shape implements
`IHasVisualBounds.GetLocalBounds()`. `Main.FitCameraToWorld()` unions
all of them and sets `Camera2D`'s position and zoom to fit the result,
accounting for the log panel's footprint at the bottom
(`UiPanelHeight`, which has to stay in sync with `LogPanel`'s height in
`Main.tscn`).

This replaced hand-computed camera position/zoom constants that had to
be manually re-derived by hand *four separate times* over the course of
this world's evolution — every zoom request, every resize, every
spacing pass, every shape change meant redoing that arithmetic with no
way to render and check it. `Main.tscn`'s `Camera2D` node intentionally
carries no position/zoom values anymore — they'd just be silently
overwritten. Growing the world further shouldn't need a camera fix as a
follow-up — if it does, that's a bug in `FitCameraToWorld()`, not a
"camera needs re-tuning" ticket.

## Collision

`NPCActor` had been calling `MoveAndSlide()` since it was written, but
nothing had an actual `CollisionShape2D` — so it was a no-op, and
NPCs/the house/each other could all fully overlap. Now:

- **`NPCActor`** — a `CircleShape2D` (radius 20). NPCs push off each
  other automatically via Godot's default physics, no extra code — both
  bodies just needed a real shape.
- **`Home`** — a `StaticBody2D` (still a `Node2D` under the hood;
  `.Position`, `_Draw()`, etc. all still work unchanged). Its collision
  matches the wall footprint only, computed from the same `WallRect`
  `_Draw()` uses so the two can't drift apart — the roof's overhang is
  visual and doesn't block anything.
- **`AppleTree`** — also `StaticBody2D`, but with a small collidable
  trunk (radius 10) at its base while the much wider visual canopy
  (radius 36) stays purely decorative — walking "under" a tree is fine,
  per the call to keep that simple for now.
- **`FishingSpot` / `River`** — no collision at all (shallow water,
  walking through is fine).

None of this conflicts with the existing interaction-`Range` values — an
NPC already stops and switches to `Attempting` well outside any of these
shapes (e.g. `ActionRanges.PickApple` is 64px; the trunk it's walking
toward is only 10px), so it never physically touches a target it's
interacting with. What collision actually changes is everything *in
between*: two NPCs converging on the same tree, or a straight-line path
that used to cut straight through the house, now resolves physically
instead of just overlapping.

One honest limitation: navigation is still "walk straight at the
target," not pathfinding — `MoveAndSlide()` slides an NPC around an
obstacle in its way rather than routing around it in advance, which is
usually fine but can occasionally cost a few extra seconds grinding
along an edge. `NPCActor.UnreachableTimeout` (10s) is the existing
safety net if that ever gets bad enough to matter.

## When the mind is unreachable: random, not scripted-deterministic

`NpcAgent.RandomFallback()` replaced an earlier fixed-priority
`ScriptedFallback()`. It still only picks among options that are
actually valid right now (never depositing nothing, never picking an
empty tree) — but *which* valid option, when there's more than one, is
random rather than "always apples first." Not personality-aware on
purpose: a network fallback isn't a real decision, so `GameAction`'s
`Emotion` stays `null` here same as before.

## Shapes and emotion

Trees are drawn as circles and home as a walled box with a triangle roof
— custom `CanvasItem` drawing, not sprites, in the original prototype
(later art layers were added on top — see `assets/CREDITS.md`).
`QueueRedraw()` is called whenever a tree's apple count changes so the
canopy color updates live.

Emotion is a fixed enum (`Emotion.cs`: Neutral, Happy, Sad, Excited,
Fearful, Angry, Curious, Content) — deliberately closed rather than free
text, so a face/emoji per value is a straightforward follow-up later.
The model reports it as a required argument on every tool call in the
same "act" request that picks the action, so it costs no extra LLM
round-trip. It's stored on `NPCActor.CurrentEmotion`, fed back into the
next turn's perception ("You are currently feeling curious"), and logged
to memory as its own entry kind.

## The feedback loop: last result and repeated failures

Diagnosed from a real play session's `logs/npc_thoughts.log`: NPCs would
retry an identical failing action (`pick_apple` while already holding
one, over a dozen times in a row) because the failure signal was only
diffusely present in `Memory.Render()` — and worse, the summarize call's
own "drop routine detail" instruction led it to compress a genuine
repeated failure into vague prose like *"yielded mixed results,"*
erasing the one fact that should have changed the NPC's next choice.
Emotions were static for the same reason — nothing prominent for the
model to react to.

Fixed with a line that lives outside `Memory` entirely, on `NpcAgent`,
so it can't be lost to a compression pass: `UpdateLastResult()` tracks
whether the same `action+target+reason` just failed again, and
`BuildPerception()` puts the result — escalating in wording once it
repeats — as the second line of every prompt, right after the persona
block. `ActInstruction` explicitly tells the model not to just repeat a
failing action and to let emotion react to what happened, not only to
personality. `SummarizeSystemPrompt` explicitly calls out a repeated
failure as the opposite of routine.

Also tightened `ThinkInstruction` — thoughts were reliably ornate ("the
gentle rustle of leaves... a balm to my frazzled nerves") because
nothing constrained tone, only length. It now asks for plain,
under-15-word, non-literary phrasing.

## Memory

`NpcMemory` records `location`, `thought`, `action`, `emotion`,
`discovery`, `speech` (what this NPC said), and `heard` (what it heard
someone else say) entries every turn. Perception carries the memory
trail alongside current state, so the mind sees what it's done as well
as what's around it. Once the raw log passes `NpcMemory.MaxLlmChars`,
`Mind.Summarize()` makes one plain-chat LLM call to fold everything into
a short diary paragraph and the raw entries are dropped — watch the log
for `...compressing memory...` followed by the new `diary:` line. If
that call itself fails, it falls back to a naive tail-truncation rather
than losing the memory or crashing.

Lives on `NpcAgent` (alongside `Mind` and the `_thinking` flag) — each
NPC has its own, which is what actually made more than one NPC possible
without every agent's memory bleeding into the others'.

## Talking and listening

`speak` is distinct from `thought` — `thought` is private (only ever
visible in the log, never to another NPC); `speak` is a broadcast any
NPC within `SpeechLog.HearingRadius` (260px, same scale as
`SpatialMemory.VisionRadius`) might hear. `SpeechLog` is a shared record
of who said what, where, and when (same decoupled pattern as
`WorldRegistry`/`PathGrid` — no direct references between `NpcAgent`s).

A few things worth being explicit about, since they were easy to get
subtly wrong:

- **Speaking never fails for lack of an audience.** It always succeeds
  and gets recorded — whether anyone actually heard it is a separate,
  honest fact checked at each listener's own next turn, not a
  precondition on the act of speaking. Talking into an empty field is a
  normal, valid outcome, not an error.
- **Each utterance is delivered to a given listener exactly once**
  (`SpeechLog.Overheard()` marks it consumed for that listener), not
  re-surfaced every turn for as long as it's retained — otherwise the
  same line would spam perception on every tick while in range.
- **Heard speech goes into the listener's own `Memory`**, not just that
  turn's live perception — an utterance heard now should still be
  rememberable several turns later even if the listener's next decision
  doesn't land for a while (LLM latency varies), not just while it
  happens to still be "live" in `SpeechLog`.
- **Nothing compels a listener to comply.** One NPC declaring "follow me
  to the mountains" is just text another NPC's mind reads in its own
  next perception — whether that produces `follow` toward them
  specifically, `travel` toward the destination independently, or
  neither, is that NPC's own LLM call to make, same "no hardcoded roles"
  posture as the rest of this project.
- **Perception lists who's nearby**, by name, with their current
  emotion, whenever another NPC is within hearing range — reading
  `WorldContext.Agents`, the same list `Main._Ready()` appends to as
  each NPC is created, referenced directly rather than copied so every
  agent sees the full, current roster once everyone actually exists.

### Names, not ids, everywhere a person or model needs to track who's who

`Id` ("npc_0") is still the stable internal key — `WorldRegistry`,
thought-log correlation, self-exclusion checks. Every human/LLM-facing
surface uses `Personality.Name` instead: console log prefixes,
`SpeechLog`, `Memory` records, perception's "who's nearby" line, and
`follow`'s target. "npc_0 said: ..." is strictly worse for a model (or a
person reading the console) to have to track than "Maren said: ...".
`NpcFactory` registers each `NPCActor` in `WorldRegistry` under *both*
its id and its display name, so `follow`'s target ("Finn") resolves
through the exact same lookup every other action already uses — no
separate name-to-id translation layer needed.

### `follow`

Targets another NPC by name — restricted to whoever's currently within
`SpeechLog.HearingRadius`, the same anti-hallucination posture as every
other target list (you can't choose to follow someone you have no way of
knowing is around). Mechanically cheap to add: `NPCActor` already
re-reads its target's live `GlobalPosition` every physics frame (that's
what makes any navigation work at all), so pointing that at another
*moving* `NPCActor` instead of a fixed resource just works — no new
movement code, only the usual "no `IInteractable`, no `PathGrid`"
carve-outs already used for `travel` (a one-time A* path to a moving
target would go stale the instant it took a step).

Deliberately **not** a commitment by default. There's no persistent "is
following" state — an NPC re-decides every single turn, informed by
whatever it's currently feeling and perceiving, same as every other
action. Choosing `follow` again next turn is what "still following"
means; choosing anything else is what "changed its mind" means. (A later
addition lets a *player request* to follow become a real multi-turn
commitment rather than a one-off nudge — see the git log around NPC
request handling.)

## The player character

`PlayerCharacter : NPCActor, IWorldCharacter` — not a parallel
reimplementation, a subclass. `NPCActor` is the tactical layer every
character shares: collision, `Inventory`, `CharacterStats`, `Vitals`,
and the whole `AssignAction()` → `ProcessNavigating()`/
`ProcessAttempting()` → `TryInteract()`/`SkillCheck` pipeline. `NpcAgent`
wraps an `NPCActor` with a `Mind` and runs its LLM turn loop.
`PlayerCharacter` wraps the *same* `NPCActor` with WASD/arrow input and a
small action panel instead — no `Mind`, because nothing needs to decide
for it. Clicking a panel button calls `AssignAction()` directly, the
exact call `NpcAgent` makes after `Mind.Decide()` returns, so every
action resolves through code an NPC's decision also runs — same ranges,
same dice, same failure reasons.

The one real behavioral difference: `PlayerCharacter._PhysicsProcess()`
always calls `base._PhysicsProcess()` first (this is what makes `Vitals`
decay and an in-progress action's navigation/attempting run identically
to an NPC — it's a harmless no-op when idle, since `NPCActor`'s state
switch has no `Idle` case), then branches on `_state == Idle` to apply
free WASD movement instead of an LLM decision. That's the entire seam
between "input-controlled" and "LLM-controlled" — everything else is
shared, unmodified, base-class behavior.

`speak` types into the chat box (Enter to broadcast, same `SpeechLog`
every NPC uses); `persuade` also borrows that box as its message source
— type the pitch, then click Persuade on whoever's nearby, since a
button click alone can't supply free text. The action panel shows only
whatever's currently valid by proximity — the exact same `ActionRanges`
an NPC's tool schema is constrained to, just answered by distance
instead of a tool call.

`StartScreen.tscn`/`StartScreen.cs` collects a name before `Main` loads,
handed across the scene change via the `PlayerState` autoload (the only
thing it carries — it is *not* the player character itself). `Main.
CreatePlayer()` disambiguates against a same-named NPC (WorldRegistry
keys by name are shared and silently overwrite on collision, which would
otherwise misdirect any `follow`/`trade`/`steal`/`persuade` aimed at that
name) before registering and adding the player to `WorldContext.Agents`
alongside every `NpcAgent`.

## Inventory, stats, and vitals

`Inventory` (multi-item counts, not a single "holding" slot — `Add`/
`Remove`/`Has`/`Snapshot`) replaced an earlier one-item-at-a-time field.
`deposit` empties the whole thing into `Home`'s stores in one call,
whatever mix of items it holds.

`CharacterStats` is a classic D&D six-stat block (STR/DEX/CON/INT/WIS/
CHA, 3d6 each) rolled fresh per character at construction — including
the player, since `PlayerCharacter` *is* an `NPCActor`. Deliberately a
separate axis from `Personality`'s OCEAN traits: personality is what a
character *wants*, stats are what they're *capable of*. `SkillCheck`
(`d20 + modifier` vs a `DifficultyClass`) is the one shared roll every
check uses — picking an apple (`DifficultyClass.Gather`, DEX, low bar
but not automatic), stealing and persuading (`DifficultyClass.
OpposedBase` + the *target's* own relevant modifier — a genuine contest,
not a flat number everyone faces alike), and later combat (see
`Combat.cs`). Every checked result carries `{check, d20, modifier,
total, dc}` back through `Finish()`/`TryInteract()`'s data dictionary,
and both `NpcAgent` and `PlayerCharacter` print it: `[dexterity check:
d20(14) + 1 = 15 vs DC 5]`, next to every gather/steal/persuade result,
success or fail.

`Vitals` (Health, Fatigue, both 0-100) decays passively every physics
frame regardless of state — that's why `PlayerCharacter` always calls
`base._PhysicsProcess()` first, even while idle — and drains faster from
real exertion (gathering actions call `Vitals.Exert()` on every attempt,
successful or fumbled). Below `Vitals.LowFatigueThreshold`, perception
plainly says so ("exhausted — needs sleep soon"), same as every stat/
inventory number a character gets — but nothing anywhere *forces* a
decision on it. An NPC turning down a trip because it's exhausted, or
pushing through anyway, is expected to fall out of the LLM reading that
line, not a hardcoded gate. `sleep` rests in place and restores fatigue
(a small health nudge too) — takes real time for an NPC, instant for the
player (a known, minor asymmetry).

`trade` and `steal` both move an item directly between two characters'
`Inventory` — nothing in between, no economy/market layer. `trade` is a
one-directional gift (always succeeds if the giver actually has it; the
recipient's agreement is never checked, same as speaking to someone who
may or may not want to hear it). `steal` is a Dexterity contest against
the target's own Dexterity modifier — it can fail even when the target
genuinely has the item (`steal_failed`, a real dice loss) or fail
outright if they don't (`target_has_none`, checked before rolling, so a
wrong guess doesn't risk "getting caught" for nothing). Nobody is ever
told directly they were stolen from — `NoticeInventoryChanges()` (on
both `NpcAgent` and `PlayerCharacter`, same mechanism, duplicated since
the player has no `Memory`/turn loop to hook into) compares a
character's own inventory against a baseline snapshotted right after its
own last action resolved; any difference found at the next check can
only be someone else's doing, and gets folded into `Memory`/the console
log as a "you notice you're missing..." or "...now have more than you
remember" line — the character has to work out for itself whether that
means lost, given, traded, or stolen, the same way a person pats their
pockets. `persuade` is an opposed Charisma check whose *only* effect is
how the message reads to the target (`SpeechLog.Say` framed as "(tries
hard to convince you)" vs "(...but doesn't seem very persuasive)") — it
never picks the target's next action for them; they still decide for
themselves, next turn, same as anything else they hear.

Two fumble-shaped failures (`fumbled`, `steal_failed`, `unconvincing`)
are deliberately *not* treated as a sign anything structural is wrong —
`NpcAgent.UpdateLastResult()` tracks them separately from real blocks
(`depleted`, `hands_full`, `target_not_found`) so two unlucky rolls in a
row on a perfectly good tree don't get the "stop repeating this, pick
something else" escalation a genuine block gets; the framing stays
"that's just bad luck, trying again is reasonable."

## Failure paths already wired up

Try these to see the fail → report → adjust loop without touching the
LLM at all:

- Set a resource's count to `0` in `Main.BuildWorld()` → that NPC's
  first gather attempt there comes back `depleted`, and the mind picks a
  different target on its next decision.
- Move a target far outside `NPCActor.UnreachableTimeout`'s reach (e.g.
  `new Vector2(5000, 5000)`) → after 6 seconds of walking, the executor
  gives up and reports `unreachable` instead of walking forever.

## Later systems (wildlife, combat, day/night, fire)

Added after the sections above were written, per the git log
(`3013781` and surrounding commits) — not yet given the same prose
write-up, but summarized here so this log doesn't read as stranded in
the apple-garden era:

- **Wildlife** (`Animal.cs` + `Bear.cs`/`Wolf.cs`/`Rabbit.cs`) — plain
  finite state machines, deliberately *not* LLM-driven ("more basic":
  wander, look for food, eat, sleep). Each species overrides
  `DecideBehavior()` with its own rules (bears prefer berries but attack
  when starving; wolves hunt rabbits and defend regardless of hunger;
  rabbits only flee, eat, and multiply within a population cap). Shares
  `ICombatant` with `NPCActor`, so a human's `attack` action and an
  animal's own attacks both resolve through the same `Combat.Resolve()`
  call.
- **Combat** (`Combat.cs`) — one formula for every fight (human vs.
  animal, animal vs. human, animal vs. animal, and nothing stops human
  vs. human later): an opposed roll, attacker's Strength+Dexterity
  against a DC built from the defender's Dexterity, with damage scaled
  by Strength and whatever weapon is equipped (`Weapons.cs`).
- **Day/night cycle** (`DayNightCycle.cs`) — a smoothly-blended
  `CanvasModulate` color (bright/neutral at midday, blue-purple at
  midnight) rather than a fixed dim value, ticking only while the world
  is unpaused.
- **Fire pits and torches** (`FirePit.cs`, `Weapons.cs`) — one fixed
  `firepit` world object that can be lit for a timed duration; a stick
  in inventory can be turned into a torch once lit, which is both a
  light source and a meaningfully stronger weapon than an unarmed hit.
- **Foraging variety** (`GatherableFoliage.cs`) — pinecones and several
  berry-bush variants share one "walk up, gather until empty, tint when
  depleted" implementation rather than each reimplementing
  `AppleTree`'s pick/deplete/exert dance.
- **Procedural world growth** (`WorldExploration.cs`) — the map grows
  the first time any character (NPC or player) enters a region nobody's
  been near before, tracked as a shared, static region grid — distinct
  from each NPC's own `SpatialMemory` of personally-recognized
  landmarks.
- **NPC request handling** — the player can hand an NPC a direct request
  (follow, trade, gather, etc.) through a dedicated decision path rather
  than it competing with the NPC's normal autonomous action menu, and an
  agreed-to `follow` can become a real multi-minute commitment instead
  of a one-off nudge.

For the reasoning behind any of these in the same level of detail as the
sections above, the commit messages (`git log`) and the in-file comments
(most files open with a multi-line "why this exists" comment, same style
as this log) are the next place to look.

## Fine-tuning the local model

A separate effort, fully documented in
[llm_tuning/README.md](llm_tuning/README.md): once real play sessions
showed `llama3.2:3b` leaning on `speak` far more than it should, two real
infra bugs got found and fixed (a silently-truncated context window, and
~5s of dead DNS time per request), a baseline eval script quantified
where the model actually stood (~79-83% zero-shot), and prompt-level
fixes closed most of the gap. What's left — reliably declining an action
when the specific thing asked for genuinely isn't available — is the
actual target of the LoRA fine-tune living in that directory.

## Next steps, as of the last pass through this log

1. Give the player's `sleep` the same real duration an NPC's gets
   (currently instant) — needs a duration concept added to
   `NPCActor.ProcessAttempting()` itself, not just `PlayerCharacter`, so
   it's a real (if contained) change to shared tactical code.
2. A second, human-controlled `PlayerCharacter` — the class already
   takes a `KeyBindings` struct per instance specifically so a second
   instance can use different keys without touching anything else; the
   remaining work is a second `StartScreen`-style entry point (or a
   two-name prompt on the one screen) and a second `Initialize()` call in
   `Main.CreatePlayer()`.
3. The player's Steal button always guesses `"apple"` — a second button
   (or a small item picker) for other items would round that out; you
   don't know a target's inventory any more than an NPC does, so this
   isn't about removing the guesswork, just not limiting it to one
   guess.
4. Watch for LLM throughput limits as more agents share one Ollama host
   — nothing currently staggers or queues concurrent requests beyond
   `LlmRequestQueue.cs`'s existing throttle; revisit if that stops being
   enough.
5. Stream the "think" call's content instead of waiting for the full
   response, once there's a UI worth streaming it into.

(This list reflects design notes accumulated alongside the sections
above — check the git log and open issues for what's actually current.)
