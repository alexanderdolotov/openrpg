# Apple Garden Prototype

A bare-bones Godot 4 scene with no art — a brown-and-triangle-roofed
home, three green circles (apple trees), a blue river with two fishing
spots, misty mountains and a forest/foothills/river-crossing obstacle
course to the north, three colored squares (Maren, Finn, Wren — LLM-
driven) and one more you control yourself — built to prove out one
thing before anything else gets touched: the loop between a "mind"
deciding *what* to do and an engine actually carrying it out, including
what happens when an attempt fails.

Every character — NPC or player — shares the same action set (gather,
deposit, travel, speak, follow, trade, steal, persuade, sleep, wait),
the same `Inventory`, the same rolled `CharacterStats` (D&D-style
STR/DEX/CON/INT/WIS/CHA) and `Vitals` (health/fatigue), and the same
dice-backed `SkillCheck` resolving anything that isn't a sure thing. The
*only* difference between an NPC and the player is what decides an
action — `Mind.Decide()` for one, keyboard/mouse for the other. See
"The player character" and "Inventory, stats, and vitals" below.

Written in **C#**, not GDScript — ported over once the core loop was
proven, since GDScript's dynamic typing had already caused two real
bugs (a method-resolution issue, a runtime type crash on malformed LLM
output) that C#'s compiler catches before you ever hit Play.

## Setup (one-time)

1. Install the **.NET SDK 8** (`brew install --cask dotnet-sdk`, or
   from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0)).
2. Install the **.NET-enabled** build of Godot 4.7 from
   [godotengine.org/download/macos](https://godotengine.org/download/macos)
   — not the Standard build; C# needs the Mono/.NET runtime bundled in.
3. Open this folder in that Godot build. It should detect the existing
   `OpenRPG.csproj` and build automatically. If it instead offers to
   create a C# solution, let it — it'll regenerate the `.csproj` with
   the exact SDK version matching your engine, which is more reliable
   than the one committed here.
4. (Optional) In Godot: Editor Settings → Dotnet → Editor → External
   Editor → **Visual Studio Code**, so double-clicking a script opens it
   there with real IntelliSense.

## Run it

Hit Play. A name prompt (`StartScreen`) comes up first — type a name and
it carries through to your character in `Main` via the `PlayerState`
autoload (skip it and run `Main.tscn` directly during development; you
just get called "Player"). Once the world loads, the three NPCs start
deciding what to do on their own — walking to a tree or a fishing spot,
picking or catching, walking home, depositing, and repeating — while you
move with WASD or arrow keys, press Enter in the chat box to speak (or
to deliver a `persuade` pitch — type it, then click Persuade on someone
nearby), and click whatever shows up in the action panel (top-left) as
you get in range of something. Watch the scrolling log docked across
the bottom of the window — every line is tagged by name, and thoughts,
attempts, and results are color-coded so you can scan it at a glance.

## Choosing an LLM backend

Default is local Ollama on `analytics.local` — no setup needed. To
point at a cloud API instead, copy `mind.example.json` to
`mind.local.json` (gitignored — never commit a real key) and edit it:

```json
{
  "provider": "openai_compatible",
  "base_url": "https://openrouter.ai/api/v1",
  "model": "anthropic/claude-3.5-sonnet",
  "api_key": ""
}
```

Leave `api_key` blank and set the `MIND_API_KEY` environment variable
instead if you don't want the key sitting in a file at all. Any
OpenAI-compatible endpoint works this way — OpenAI itself, Groq,
Together, or OpenRouter (which alone exposes Claude, GPT, and Llama
variants through one key, handy for actually comparing them). Set
`provider` back to `"ollama"` to go local again.

## What's actually being tested

`scripts/Mind.cs` builds the prompts and validates whatever comes back,
completely independent of who's answering; `scripts/OllamaProvider.cs`
and `scripts/OpenAiCompatibleProvider.cs` are the two swappable
backends, chosen by `MindConfig` at startup. Whichever one is active,
the three actions (`pick_apple`, `deposit_apple`, `wait`) are defined as
tools, and the tree ids are built into the schema each call so the
model can't target one that doesn't exist. The "think" call's `content`
is logged as the NPC's thought; the "act" call's `tool_calls[0]`, once
validated, becomes the next `GameAction`.

If anything fails — the backend unreachable, a timeout, an unrecognized
action name, a target that isn't currently real — `NpcAgent`'s
`RandomFallback()` picks the next action instead: hands full? deposit.
Otherwise, a random pick among whatever's actually available (an apple
tree with fruit left, a fishing spot with fish left). The log says
which path produced each action.

Everything else is the permanent part:

- **`GameAction`** — what the mind hands down: an action id and a
  target id (never coordinates). `pick_apple` targeting `tree_1`, not
  "move to (620, 220) then pick."
- **`NPCActor`** — the tactical executor. Given a `GameAction` it walks
  toward the target, checks range every physics frame, retries the
  approach on its own, and only reports back (`ActionCompleted`) once
  the action is truly resolved. The mind is never asked "should I move
  closer?" — that's handled silently, every frame, by the executor.
- **`IInteractable`** (`AppleTree` / `Home`) — each target owns its own
  rules via `TryInteract()`. In the GDScript version this was
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
  explicit ok/error result the caller must check, rather than leaning
  on exceptions.
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

## Two NPCs, no hardcoded roles

Maren and Finn are both built through `NpcFactory.Create()`, which
assembles the full bundle — `NPCActor`, a `Mind` with its own
`ILlmProvider` instance (never shared between agents; one `HttpRequest`
node can't have two requests in flight at once), an `NpcMemory`, and a
`Personality` — into one `NpcAgent`. Adding an NPC is one factory call
against a `WorldContext` (the shared trees/fishing spots/home), not a
rewrite.

Critically: `Mind.BuildTools()` hands every NPC the *identical* tool
menu — `pick_apple`, `catch_fish`, `deposit`, `wait`. Nothing in
`NpcAgent`, `NpcFactory`, or `Mind` branches on which NPC is "the
fisher." The only thing that differs between Maren and Finn is the text
their `Personality.DescribeForPrompt()` puts in the system prompt —
Finn's backstory leans toward water, Maren's doesn't — and whatever role
that produces is the model's own inference from that framing each turn,
not a code path. Watch both NPCs' logs; there's no guarantee either one
picks the role you'd expect, which is the point.

`Personality` (`Personality.cs`) also does two other jobs: its five
traits (openness, conscientiousness, extraversion, agreeableness,
neuroticism, each 0-1) get worded into the persona block Word-by-value
(e.g. openness 0.7 → "very curious and open to new things"), and they
derive a sampling `Temperature` — steadier/conscientious NPCs sample
cold, open/neurotic ones sample hot — finally wiring up the
personality-driven-temperature idea the architecture doc always
intended but never implemented until now.

### Editing the roster without touching code

Every NPC's id, name, backstory, start position, and visual color live
in **`npcs.json`** (checked into git — this is game content, not a
secret like `mind.local.json`). `Main.cs` just loops over whatever
`NpcRoster.Load()` returns and calls `NpcFactory.Create()` per entry —
adding a third NPC is an edit to that file, not a recompile. Missing or
malformed `npcs.json` falls back to Maren and Finn's current values
baked into `NpcRoster.Defaults`, so the game never ends up with zero
NPCs.

Personality traits themselves aren't spelled out per NPC, though —
**`npcs.json`** references a named shape from **`personality_archetypes.json`**
(via the `archetype` field) instead. Each archetype is a reusable trait
set with a human-readable `description` (not sent to the LLM — only the
numbers matter there) — `steady_thinker`, `easygoing_wanderer`,
`cautious_homebody`, `bold_adventurer`, `gruff_loner`,
`warm_socializer`, `anxious_worrier`, `calm_pragmatist`, defined in
`ArchetypeLibrary.cs`'s built-in fallback and mirrored in the checked-in
file. Maren is `steady_thinker`, Finn is `easygoing_wanderer` — same
numbers either had before this split, just named and reusable now. An
unknown or misspelled archetype id falls back to `calm_pragmatist`
rather than crashing NPC creation. Adding a new NPC that reuses an
existing shape is now just `{"archetype": "gruff_loner", ...}`; adding
an entirely new shape is one more object in `personality_archetypes.json`.

(This is JSON, not YAML — kept consistent with `npcs.json` and
`mind.local.json`, and avoids adding a third-party YAML dependency for
a handful of files.)

## The world: garden + river

`River` is purely visual (`_Draw()`, no interaction). `FishingSpot` is
`AppleTree`'s sibling — same `IInteractable` shape, `catch_fish` instead
of `pick_apple`, its own depleted-color feedback. `NPCActor.Holding` is
now a string (`""` / `"apple"` / `"fish"`) instead of an apple-specific
bool, and `Home.TryInteract` takes one generic `"deposit"` action that
banks whatever's being carried — so the tool schema doesn't grow
linearly with every new resource type added later.

`River` is a meandering ribbon, not a rectangle — a sine-wave
centerline (`Length`, `Amplitude`, `Waves`) with banks (`Thickness`)
computed perpendicular to the curve's tangent at each sample point, so
it actually reads as winding rather than a wobbly bar. `FishingSpot`
placement calls `river.GetCenterlineWorldY(x)` to sit exactly on the
curve, rather than guessing a fixed y that would land on the bank at
some points along the wave.

### The camera fits itself to the world now

Every world object with a real shape — `Home`, `AppleTree`,
`FishingSpot`, `River` — implements `IHasVisualBounds.GetLocalBounds()`.
`Main.FitCameraToWorld()` unions all of them and sets `Camera2D`'s
position and zoom to fit the result, accounting for the log panel's
footprint at the bottom (`UiPanelHeight`, which has to stay in sync
with `LogPanel`'s height in `Main.tscn`).

This replaced hand-computed camera position/zoom constants that had to
be manually re-derived by hand *four separate times* over the course of
this world's evolution — every zoom request, every resize, every
spacing pass, every shape change meant redoing that arithmetic with no
way to render and check it. The camera now adapts on its own; `Main.tscn`'s
`Camera2D` node intentionally carries no position/zoom values anymore —
they'd just be silently overwritten. Growing the world further (a
fourth resource type, a bigger river, more NPCs) shouldn't need a
camera fix as a follow-up anymore — if it does, that's a bug in
`FitCameraToWorld()`, not a "camera needs re-tuning" ticket.

## Collision

`NPCActor` has been calling `MoveAndSlide()` since it was written, but
nothing had an actual `CollisionShape2D` — so it was a no-op, and
NPCs/the house/each other could all fully overlap. Now:

- **`NPCActor`** — a `CircleShape2D` (radius 20). NPCs push off each
  other automatically via Godot's default physics, no extra code —
  both bodies just needed a real shape.
- **`Home`** — changed from `Node2D` to `StaticBody2D` (still a
  `Node2D` under the hood; `.Position`, `_Draw()`, etc. all still work
  unchanged). Its collision matches the wall footprint only, computed
  from the same `WallRect` `_Draw()` uses so the two can't drift apart
  — the roof's overhang is visual and doesn't block anything.
- **`AppleTree`** — also `StaticBody2D`, but with a small collidable
  trunk (radius 10) at its base while the much wider visual canopy
  (radius 36) stays purely decorative — walking "under" a tree is fine,
  per the call to keep that simple for now.
- **`FishingSpot` / `River`** — no collision at all, unchanged (shallow
  water, walking through is fine).

None of this conflicts with the existing interaction-`Range` values —
an NPC already stops and switches to `Attempting` well outside any of
these shapes (e.g. `ActionRanges.PickApple` is 64px; the trunk it's
walking toward is only 10px), so it never physically touches a target
it's interacting with. What collision actually changes is everything
*in between*: two NPCs converging on the same tree, or a straight-line
path that used to cut straight through the house, now resolves
physically instead of just overlapping.

One honest limitation: navigation is still "walk straight at the
target," not pathfinding — `MoveAndSlide()` slides an NPC around an
obstacle in its way rather than routing around it in advance, which is
usually fine but can occasionally cost a few extra seconds grinding
along an edge. `NPCActor.UnreachableTimeout` (10s) is the existing
safety net if that ever gets bad enough to matter.

## When the mind is unreachable: random, not scripted-deterministic

`NpcAgent.RandomFallback()` replaced the old fixed-priority
`ScriptedFallback()`. It still only picks among options that are
actually valid right now (never depositing nothing, never picking an
empty tree) — but *which* valid option, when there's more than one, is
random rather than "always apples first." Not personality-aware on
purpose: a network fallback isn't a real decision, so `GameAction`'s
`Emotion` stays `null` here same as before.

## Shapes and emotion

Trees are drawn as circles (`AppleTree._Draw()`) and home as a walled
box with a triangle roof (`Home._Draw()`) — custom `CanvasItem` drawing,
not sprites, so still no art assets. `QueueRedraw()` is called whenever
a tree's apple count changes so the canopy color updates live.

Emotion is a fixed enum (`Emotion.cs`: Neutral, Happy, Sad, Excited,
Fearful, Angry, Curious, Content) — deliberately closed rather than free
text, so a face/emoji per value is a straightforward follow-up later.
The model reports it as a required argument on every tool call in the
same "act" request that picks the action, so it costs no extra LLM
round-trip. It's stored on `NPCActor.CurrentEmotion`, fed back into the
next turn's perception ("You are currently feeling curious"), and
logged to memory as its own entry kind.

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
block. `ActInstruction` now explicitly tells the model not to just
repeat a failing action and to let emotion react to what happened, not
only to personality. `SummarizeSystemPrompt` now explicitly calls out a
repeated failure as the opposite of routine.

Also tightened `ThinkInstruction` — thoughts were reliably ornate
("the gentle rustle of leaves... a balm to my frazzled nerves") because
nothing constrained tone, only length. It now asks for plain,
under-15-word, non-literary phrasing.

## Memory

`NpcMemory` records `location`, `thought`, `action`, `emotion`,
`discovery`, `speech` (what this NPC said), and `heard` (what it heard
someone else say) entries every turn. Perception now carries the memory
trail alongside current state, so the mind sees what it's done as well
as what's around it. Once the raw log passes `NpcMemory.MaxLlmChars`,
`Mind.Summarize()` makes one plain-chat LLM call to fold everything into
a short diary paragraph and the raw entries are dropped — watch the log
for `...compressing memory...` followed by the new `diary:` line. If
that call itself fails, it falls back to a naive tail-truncation rather
than losing the memory or crashing.

Lives on `NpcAgent` now (alongside `Mind` and the `_thinking` flag) —
each NPC has its own, which is what actually made a second NPC possible
without every agent's memory bleeding into the others'.

## Talking and listening

`speak` is a new action, distinct from `thought` — `thought` is private
(only ever visible in the log, never to another NPC); `speak` is a
broadcast any NPC within `SpeechLog.HearingRadius` (260px, same scale as
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
- **Nothing compels a listener to comply.** Wren declaring "follow me to
  the mountains" is just text another NPC's mind reads in its own next
  perception — whether that produces `follow` toward Wren specifically,
  `travel` toward `misty_mountains` independently, or neither, is that
  NPC's own LLM call to make, same "no hardcoded roles" posture as the
  rest of this project.
- **Perception now also lists who's nearby**, by name, with their
  current emotion, whenever another NPC is within hearing range —
  reading `WorldContext.Agents`, the same list `Main._Ready()` appends
  to as each NPC is created, referenced directly rather than copied so
  every agent sees the full, current roster once everyone actually
  exists.

### Names, not ids, everywhere an NPC (or a human reading the log) needs
### to track who's who

`Id` ("npc_0") is still the stable internal key — `WorldRegistry`,
thought-log correlation, self-exclusion checks. Every human/LLM-facing
surface uses `Personality.Name` instead: console log prefixes,
`SpeechLog`, `Memory` records, perception's "who's nearby" line, and
`follow`'s target. "npc_0 said: ..." is strictly worse for a model (or a
person reading the console) to have to track than "Maren said: ...".
`NpcFactory` now registers each `NPCActor` in `WorldRegistry` under
*both* its id and its display name, so `follow`'s target ("Finn") 
resolves through the exact same lookup every other action already uses
— no separate name-to-id translation layer needed.

### `follow`

Targets another NPC by name — restricted to whoever's currently within
`SpeechLog.HearingRadius`, the same anti-hallucination posture as every
other target list (you can't choose to follow someone you have no way
of knowing is around). Mechanically cheap to add: `NPCActor` already
re-reads its target's live `GlobalPosition` every physics frame (that's
what makes any navigation work at all), so pointing that at another
*moving* `NPCActor` instead of a fixed resource just works — no new
movement code, only the usual "no `IInteractable`, no `PathGrid`" carve
-outs already used for `travel` (a one-time A* path to a moving target
would go stale the instant it took a step).

Deliberately **not** a commitment. There's no persistent "is following"
state anywhere — an NPC re-decides every single turn, informed by
whatever it's currently feeling and perceiving, same as every other
action. Choosing `follow` again next turn is what "still following"
means; choosing anything else is what "changed its mind" means. Nothing
extra had to be built for "later they might stop following or need
convincing again" — it falls straight out of the existing per-turn
re-decision loop.

## The player character

`PlayerCharacter : NPCActor, IWorldCharacter` — not a parallel
reimplementation, a subclass. `NPCActor` is the tactical layer every
character shares: collision, `Inventory`, `CharacterStats`, `Vitals`,
and the whole `AssignAction()` → `ProcessNavigating()`/
`ProcessAttempting()` → `TryInteract()`/`SkillCheck` pipeline. `NpcAgent`
wraps an `NPCActor` with a `Mind` and runs its LLM turn loop.
`PlayerCharacter` wraps the *same* `NPCActor` with WASD/arrow input and
a small action panel instead — no `Mind`, because nothing needs to
decide for it. Clicking a panel button calls `AssignAction()` directly,
the exact call `NpcAgent` makes after `Mind.Decide()` returns, so
`pick_apple`/`catch_fish`/`deposit`/`travel`/`follow`/`trade`/
`steal`/`persuade`/`sleep` all resolve through code an NPC's decision
also runs — same ranges, same dice, same failure reasons.

The one real behavioral difference: `PlayerCharacter._PhysicsProcess()`
always calls `base._PhysicsProcess()` first (this is what makes
`Vitals` decay and an in-progress action's navigation/attempting run
identically to an NPC — it's a harmless no-op when idle, since
`NPCActor`'s state switch has no `Idle` case), then branches on
`_state == Idle` to apply free WASD movement instead of an LLM
decision. That's the entire seam between "input-controlled" and
"LLM-controlled" — everything else is shared, unmodified, base-class
behavior.

`speak` types into the chat box (Enter to broadcast, same `SpeechLog`
every NPC uses); `persuade` also borrows that box as its message source
— type the pitch, then click Persuade on whoever's nearby, since a
button click alone can't supply free text. The action panel
(`Main.tscn`'s `UI/ActionPanel`) shows only whatever's currently valid
by proximity — the exact same `ActionRanges` an NPC's tool schema is
constrained to, just answered by distance instead of a tool call.

`StartScreen.tscn`/`StartScreen.cs` collects a name before `Main` loads,
handed across the scene change via the `PlayerState` autoload (the only
thing it carries — it is *not* the player character itself). `Main.
CreatePlayer()` disambiguates against a same-named NPC (WorldRegistry
keys by name are shared and silently overwrite on collision, which
would otherwise misdirect any `follow`/`trade`/`steal`/`persuade` aimed
at that name) before registering and adding the player to
`WorldContext.Agents` alongside every `NpcAgent`.

## Inventory, stats, and vitals

`Inventory` (multi-item counts, not a single "holding" slot — `Add`/
`Remove`/`Has`/`Snapshot`) replaced the old one-item-at-a-time field.
`deposit` now empties the whole thing into `Home`'s stores in one call,
whatever mix of items it holds.

`CharacterStats` is a classic D&D six-stat block (STR/DEX/CON/INT/WIS/
CHA, 3d6 each) rolled fresh per character at construction — including
the player, since `PlayerCharacter` *is* an `NPCActor`. Deliberately a
separate axis from `Personality`'s OCEAN traits: personality is what a
character *wants*, stats are what they're *capable of*. `SkillCheck`
(`d20 + modifier` vs a `DifficultyClass`) is the one shared roll every
check uses — picking an apple (`DifficultyClass.Gather`, DEX, low bar
but not automatic), stealing and persuading (`DifficultyClass.
OpposedBase` + the *target's* own relevant modifier — a genuine
contest, not a flat number everyone faces alike). Every checked result
carries `{check, d20, modifier, total, dc}` back through
`Finish()`/`TryInteract()`'s data dictionary, and both `NpcAgent` and
`PlayerCharacter` print it: `[dexterity check: d20(14) + 1 = 15 vs DC
5]`, next to every gather/steal/persuade result, success or fail.

`Vitals` (Health, Fatigue, both 0-100) decays passively every physics
frame regardless of state — that's why `PlayerCharacter` always calls
`base._PhysicsProcess()` first, even while idle — and drains faster
from real exertion (`AppleTree`/`FishingSpot` call `Vitals.Exert()` on
every gather attempt, successful or fumbled). Below
`Vitals.LowFatigueThreshold`, perception plainly says so ("exhausted —
needs sleep soon"), same as every stat/inventory number a character
gets ("Your natural abilities: ...", "Your physical condition: ..." in
`BuildPerception()`) — but nothing anywhere *forces* a decision on it.
An NPC turning down a trip to the mountains because it's exhausted, or
pushing through anyway, is expected to fall out of the LLM reading that
line, not a hardcoded gate. `sleep` is a new always-available action
that rests in place and fully restores fatigue (a small health nudge
too) — takes 4 real seconds for an NPC, instant for the player (a
known, minor asymmetry — the player's `sleep` never got the same
duration NPCs' does, since that would mean adding a duration concept to
`NPCActor.ProcessAttempting()` itself, not just `PlayerCharacter`).

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
character's own inventory against a baseline snapshotted right after
its own last action resolved; any difference found at the next check
can only be someone else's doing, and gets folded into `Memory`/the
console log as a "you notice you're missing..." or "...now have more
than you remember" line — the character has to work out for itself
whether that means lost, given, traded, or stolen, the same way a
person pats their pockets. `persuade` is an opposed Charisma check
whose *only* effect is how the message reads to the target
(`SpeechLog.Say` framed as "(tries hard to convince you)" vs "(...but
doesn't seem very persuasive)") — it never picks the target's next
action for them; they still decide for themselves, next turn, same as
anything else they hear.

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

- Set a tree's `AppleCount` to `0` in `Main.BuildWorld()` → that NPC's
  first pick attempt there comes back `depleted`, and the mind picks a
  different tree on its next decision.
- Move a tree far outside `NPCActor.UnreachableTimeout`'s reach (e.g.
  `new Vector2(5000, 5000)`) → after 6 seconds of walking, the executor
  gives up and reports `unreachable` instead of walking forever.

## Before running it

Confirm `analytics.local:11434` is reachable and has the model, e.g.:

```
curl http://analytics.local:11434/api/chat -d '{
  "model": "llama3.2:3b",
  "messages": [{"role": "user", "content": "say hi in five words"}],
  "stream": false
}'
```

If that hangs or errors, fix the network path before hitting Play — the
game will just fall back to scripted behavior and log why, which is a
fine way to confirm the fallback works, but not what you're here to see.
Once a request from the game succeeds, `ollama ps` on `analytics.local`
should show `llama3.2:3b` loaded for the `keep_alive` window (30 min).

## Next steps, in order

1. Give the player's `sleep` the same real duration an NPC's gets
   (currently instant — see "Inventory, stats, and vitals" above) —
   needs a duration concept added to `NPCActor.ProcessAttempting()`
   itself, not just `PlayerCharacter`, so it's a real (if contained)
   change to shared tactical code, not a one-file patch.
2. A second, human-controlled `PlayerCharacter` — the class already
   takes a `KeyBindings` struct per instance specifically so a second
   instance can use different keys without touching anything else; the
   remaining work is a second `StartScreen`-style entry point (or a
   two-name prompt on the one screen) and a second `Initialize()` call
   in `Main.CreatePlayer()`.
3. Health currently only moves via `Vitals.Sleep()`'s small regen —
   there's no damage source yet to make it mean much. Wild bears (an
   original idea for "give the player something to do," since overtaken
   by the fuller action set) would be one obvious way to make it matter.
4. The player's Steal button always guesses `"apple"` — a second button
   (or a small item picker) for `"fish"` would round that out; you don't
   know a target's inventory any more than an NPC does, so this isn't
   about removing the guesswork, just not limiting it to one guess.
5. Now that there are three NPCs *and* a player sharing one Ollama host,
   watch for throughput limits if a fourth agent gets added — nothing
   here staggers or queues concurrent requests yet (§06 of the
   architecture doc's "Cognition Scheduler" is still unbuilt). Fine at
   the current count; revisit before going much higher.
6. Stream the "think" call's content instead of waiting for the full
   response, once there's a UI worth streaming it into.
