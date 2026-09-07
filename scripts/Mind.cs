using System;
using System.Threading.Tasks;

// The strategic layer's orchestration — two sequential calls per
// decision (a plain-chat "think" call, then a tool-calling "act" call
// grounded in that thought) plus validation of whatever comes back.
// Completely independent of which LLM answers: swap the ILlmProvider
// passed to the constructor and nothing in here changes.
//
// The two-call split exists because a single request carrying both a
// prose instruction AND a tools array reliably comes back with
// content=="" — verified directly against llama3.2:3b over curl. The
// model commits fully to the tool-call format and drops the sentence,
// nudged or not. This isn't provider-specific; assume it holds for any
// small tool-calling model until proven otherwise.
//
// Every action here — gathering, deposit, travel, speak, follow, trade,
// steal, sleep, wait — is offered identically to every NPC's tool schema
// (some gated on context: travel/follow/trade/steal only appear when
// there's actually a valid target, never as a dead enum-of-nothing
// option). Nothing in this file, or anywhere else, decides which NPC
// "is" the fisher, or the thief, or the leader. Whatever role an NPC
// settles into is purely a consequence of the model reading its
// Personality.DescribeForPrompt() framing and choosing accordingly.
//
// Deliberately no "persuade" action — persuasion isn't a thing you DO
// to someone with a dice roll, it's an outcome of what you actually
// say. speak already carries any pitch, argument, or appeal a
// character wants to make, word for word; whoever hears it (SpeechLog,
// folded into their own perception next turn) decides for themselves,
// by genuinely reasoning about it, whether they're convinced — the
// same way they decide how to react to anything else they hear. A
// separate mechanical action with its own Charisma check would just be
// a coin flip standing in front of that reasoning instead of letting
// it happen.
public class Mind
{
    // Instruction only — "how to respond." "Who you are" comes from
    // Personality.DescribeForPrompt(), prepended per-call in Decide()
    // so the same instruction serves any NPC's personality.
    private const string ThinkInstruction =
        "Given the situation below, write ONE short, plain sentence (under 15 words) of what you're actually thinking right now. Talk like a real person thinking to themselves, not a novelist — no metaphors, no describing the scenery, no flowery language. If your last action failed, especially more than once, say so plainly and react to it. If the situation shows someone just said something to you — especially a direct request, like asking you to follow them or help with something — that's the single most important thing to react to right now, above anything else going on: think about THAT, specifically (agreeing, refusing, or being unsure are all real reactions — silently ignoring it and thinking about something else entirely is not, unless something more urgent is actively happening to you). Just the plain thought, nothing else.";
    private const string ActInstruction =
        "Call exactly one of the provided tools that matches the plan below. If you were already in the middle of something — following someone, traveling somewhere, working toward a goal you'd set for yourself — lean toward sticking with it for a while rather than switching every single turn just because you technically can; a goal worth having is worth a bit of follow-through. Only actually change course when it's genuinely finished, clearly not working out, or something that actually matters more just happened — a real reason, not a passing whim. (This call is never even reached while something dangerous — a wolf or bear actively coming for you — is happening; see DecideThreatResponse below for that entirely separate, narrower decision.) If your last action failed — especially if it failed more than once in a row for the same reason — do not just repeat it; pick something that actually addresses why it failed (e.g. deposit before trying to pick or catch again, or choose a different target if one is depleted). travel is a valid choice on its own, out of curiosity, even toward somewhere you've never been and don't know the way to — you don't need a resource-gathering reason to go look at something. speak lets you say something out loud in your own words — anyone within hearing range right now may hear it and decide how to react on their own later, including being persuaded, won over, or talked into something if what you say actually lands with them; it does not control or compel anyone, and if nobody's around, nobody hears it, which is a perfectly normal outcome of speaking. follow lets you walk alongside someone nearby by name — a genuine choice you make (or don't) based on your own read of them and what's been said, not something anyone can force; re-decide it fresh every turn just like anything else, which means choosing follow AGAIN, turn after turn, is what actually keeps you with them — arriving next to them once doesn't mean you're done, they may well walk on right after, and only choosing something else is what actually stops you following. If another character (not the player) just directly asked you to follow them or help with something, that request is the main thing to weigh right now — follow (if you're actually persuaded) or speak (to say yes, no, or ask something back) are both real, direct responses to it; picking something completely unrelated, like gathering, is turning them down without saying so, which is a worse look than an honest no. (A direct ask from the real human player never actually reaches this decision at all — it's answered separately, immediately, the turn it's heard; see DecidePlayerRequest.) trade gives someone nearby an item you're actually carrying — a real choice about generosity or self-interest, entirely up to you. steal takes an item from someone nearby without asking and without them agreeing to it — you don't know for certain what they're carrying, only a guess, it takes real nerve and a little luck (you can simply fail even if they do have it), and it's not hidden from them forever, since anyone keeps their own count of what they're carrying and can notice later that something's missing. sleep rests where you are and fully restores your fatigue (and health), but takes a while — worth doing once you're actually tired, not as a routine choice, and pay attention to your own fatigue level below: if you're exhausted, that's a real, physical reason to sleep before doing anything else, or to turn down something demanding (a long trip, more gathering) rather than push through it — nobody is forcing that consideration on you, it's just true of your own body right now. eat restores health and hunger from something you're already carrying — but pay attention to your health and hunger levels below even when eat ISN'T offered yet: if either is getting low and you're not carrying any food, that's a real, physical reason to go gather something you can actually eat (an apple, a fish, or a berry — a pinecone doesn't count, it's not food) before continuing with whatever else you were doing, the same way low fatigue is a real reason to go sleep. pick_up_stick is worth doing any time you notice one lying around, not just when you're already thinking about a fight — it's a real upgrade over bare hands, cheap to grab in passing. attack isn't only self-defense — hunting a wild animal (a rabbit, for its meat and fur) is a completely ordinary thing to go do on your own initiative when you want food or have nothing better in mind, the same as choosing to gather fruit or catch a fish; you don't need to already be in danger to pick it as your goal for the turn. light_fire (lighting the fire pit near home) and, once it's burning, make_torch (turning a stick you're carrying into a real personal light source) and cook_meat (turning raw rabbit meat — not edible on its own — into cooked meat, the single most filling food there is) are all genuinely worthwhile things to go do, not just emergency tools: tending the fire, making yourself a torch, or cooking up what you hunted are all perfectly normal reasons to head home for a while. Set emotion to how you're genuinely feeling, reacting to what just happened as much as your personality — frustration or disappointment after a repeated failure, satisfaction after a success, not a fixed mood. Only ever target an id that is explicitly listed in the situation — never invent one that isn't there. Choose whichever tool actually fits who you are and what you want right now — nothing assigns you a role.";
    // A completely separate, deliberately narrow instruction from
    // ActInstruction above — "the LLM can't choose to go pick berries
    // while a wolf is attacking them." Only ever used by
    // DecideThreatResponse(), which offers exactly three tools (fight/
    // flee/freeze), nothing else, and skips the usual think-then-act
    // two-call split entirely — a "how am I feeling about my life
    // right now" sentence isn't worth the extra round trip when
    // something is actively trying to hurt this character and
    // NpcAgent's own instant reflex is already acting in the
    // meantime.
    private const string ThreatInstruction =
        "You are in immediate physical danger — a wild animal is actively coming for you or attacking you right now, described in the situation below. Choose exactly one of three responses: fight back, flee (run for it), or freeze (hold still, or cry out for help, rather than acting decisively). There is no fourth option — you cannot gather, travel, or do anything else right now. Weigh your own health and strength honestly: badly hurt, weak, or facing more than one attacker at once are all real reasons to flee or freeze rather than fight. A confident, strong, or cornered character may reasonably choose to fight. Pick whichever genuinely fits your personality and this exact situation.";

    // The ally version of ThreatInstruction above — same three tools
    // (fight/flee/freeze, reused rather than duplicated — see
    // BuildThreatTools), but "fight" means going to help, not
    // defending yourself, since it isn't this character's own fight.
    private const string AllyThreatInstruction =
        "A wild animal is actively attacking someone nearby right now — a friend, not you — described in the situation below. Choose exactly one of three responses: fight (go help them fight it off), flee (leave the area, put the danger behind you), or freeze (stay right where you are and just watch, doing nothing either way). There is no fourth option — you cannot gather, travel, or do anything else right now. This is a real choice about courage, not survival: weigh your own strength, health, and how brave you genuinely are. A strong, loyal, or brave character has a real reason to go help. A weak, badly hurt, or very fearful one has a real reason to hang back or leave instead — nobody should feel obligated to risk their life for someone else's fight. Pick whichever genuinely fits your personality and this exact situation.";

    // Not a narrowed tool set like ThreatInstruction above — the same
    // full menu BuildTools() always offers is still on the table here
    // (agreeing to "let's go fishing" or "come pick apples with me"
    // needs the real catch_fish/pick_apple/travel/follow/... tool, not
    // a synthetic yes/no with no way to say WHAT you're agreeing to).
    // What's different is skipping the normal think-then-act split
    // (Decide() above) and swapping in this much more insistent
    // instruction on its own — ActInstruction already asks, at length,
    // for a direct reply to the player, but that's one soft nudge
    // sitting alongside fourteen tools a small model is just as free to
    // reach for instead, and in practice (see the thought log) it does:
    // "thinking about" a direct request for several turns running
    // without ever actually acting on it, because gathering or
    // wandering off kept winning the vote. Called only for the one
    // turn the player is actually waiting on an answer (see
    // NpcAgent.HandleDirectPlayerRequest) — narrowing the INSTRUCTION
    // rather than the tool list is what keeps "yes, let's fish" and
    // "no thanks" both genuinely available without reopening the door
    // to the drift this exists to close off.
    private const string PlayerRequestInstruction =
        "The real human player — not another character in this world — just said something to you directly, described in the situation below. Your only job this turn is to answer them, right now, with a real tool call. First figure out which of two things this actually is: a QUESTION, or a REQUEST to do something. If they asked a question — about you, what you're carrying, your stats, your condition, where you are, what you're doing, anything you'd genuinely know the answer to — call speak and give the real, specific answer, using the actual information already provided to you below (your inventory, your natural abilities, your physical condition, your location) — an exact count or fact if you have one (\"I have 3 apples\"), not a vague deflection, a change of subject, or an unrelated action like follow/travel/wait; if you truly don't know or don't have whatever they're asking about, say THAT plainly (\"I don't have any\"), which is still a real, honest answer. If instead they're asking you to DO something, decide whether you're going along with it: call whichever single tool actually matches it — follow to walk with them, catch_fish/pick_apple/gather_pinecone/gather_berry to do the activity they proposed together, travel if they're inviting you somewhere, trade if they asked for an item, or whatever else genuinely fits what they said — or, if you're not going along with it or you're genuinely unsure, call speak and say so directly, in your own words; a plain, honest no is a real answer there too. Weigh it honestly against your personality and whatever else is going on, same as any other choice, but reaching for something UNRELATED to what they actually said or asked — gathering on your own, wandering off, waiting, following someone when they asked a question — is not a real option right now: that's dodging, not answering, and a worse look than an honest no or \"I don't know.\"";

    private const string SummarizeSystemPrompt =
        "You are compressing an NPC's memory log into a short diary paragraph (3-5 sentences) they'll carry forward. Preserve what matters for future decisions — where they've been, what they've done, anything notable, and anything said aloud (by them or heard from someone else), including who said or asked for what by name. A repeated identical failure is NOT routine detail — it's the opposite: state plainly what failed, why, and how many times, so it isn't attempted again pointlessly. Drop only genuinely routine, non-repeated detail (a single successful wait, a normal walk). Write in first person, past tense.";

    private static readonly string[] ValidActions = { "pick_apple", "catch_fish", "gather_pinecone", "gather_berry", "deposit", "travel", "speak", "follow", "trade", "steal", "attack", "eat", "pick_up_stick", "sleep", "wait", "light_fire", "make_torch", "cook_meat" };

    // Every item type that currently exists in the world — trade/steal
    // both need a fixed, enumerable answer to "which item" for the tool
    // schema. Grows the day a new resource type does, same as
    // ActionRanges already does per-action. Internal, not private —
    // PlayerCharacter's own steal button rolls its blind guess from
    // this exact same list, so a human player is guessing from the
    // same pool an NPC's steal tool-call enum offers, not a
    // hand-picked subset.
    public static readonly string[] ItemTypes = { "apple", "fish", "pinecone", "blueberry", "blackberry", "raspberry", "stick", "rabbit_meat", "fur", "torch", "cooked_meat" };

    private readonly ILlmProvider _provider;

    public Mind(ILlmProvider provider)
    {
        _provider = provider;
    }

    // Bundles every "what's actually available to choose from right
    // now" list Decide()/BuildTools()/ParseToolCall() all need — this
    // used to be seven-plus positional string[]/bool parameters passed
    // identically through all three, which was already unwieldy before
    // gather_pinecone/gather_berry needed two more. Plain settable
    // fields + object-initializer construction (same convention as
    // WorldContext), not a positional constructor — that stopped
    // scaling once attack/eat/pick_up_stick needed three more fields
    // apiece on top of the original eight.
    public class AvailableTargets
    {
        public string[] TreeIds;
        public string[] FishingSpotIds;
        public string[] PineTreeIds;
        public string[] BerryBushIds;
        public string[] TravelTargetIds;
        public string[] NearbyNpcNames;
        public string[] CarriedItems;
        public bool SleepAllowed;

        // Nearby, attackable animals — "attack"'s own target-id enum.
        public string[] AnimalIds;
        // Sticks on the ground nearby — "pick_up_stick"'s target-id enum.
        public string[] StickIds;
        // NPCActor.CanEat() — same "offered only when it's a real
        // option" treatment SleepAllowed already gets.
        public bool EatAllowed;

        // FirePit.IsLit's own inverse/self — light_fire only offered
        // when it's NOT already lit, make_torch only when it IS lit
        // AND this character is actually carrying a stick to light.
        public bool LightFireAllowed;
        public bool MakeTorchAllowed;
        // Same shape as MakeTorchAllowed — lit fire pit, plus actually
        // carrying the specific raw ingredient (rabbit_meat here,
        // stick there).
        public bool CookMeatAllowed;
    }

    public readonly struct MindResult
    {
        public readonly bool Ok;
        public readonly string Error;
        public readonly string Thought;
        public readonly GameAction Action;

        private MindResult(bool ok, string error, string thought, GameAction action)
        {
            Ok = ok;
            Error = error;
            Thought = thought;
            Action = action;
        }

        public static MindResult Fail(string error, string thought = "") => new(false, error, thought, null);
        public static MindResult Success(string thought, GameAction action) => new(true, null, thought, action);
    }

    public async Task<MindResult> Decide(string perceptionText, AvailableTargets targets, Personality personality)
    {
        string persona = personality.DescribeForPrompt();
        float temperature = personality.Temperature;

        var thinkMessages = new object[]
        {
            new { role = "system", content = $"{persona}\n\n{ThinkInstruction}" },
            new { role = "user", content = perceptionText },
        };
        ChatResult thinkResult = await _provider.Chat(thinkMessages, null, temperature);
        if (!thinkResult.Ok)
            return MindResult.Fail($"think_{thinkResult.Error}");

        string thought = (thinkResult.Message.Content ?? "").Trim();

        var actMessages = new object[]
        {
            new { role = "system", content = $"{persona}\n\n{ActInstruction}" },
            new { role = "user", content = $"{perceptionText}\n\nYour plan: {thought}" },
        };
        object[] tools = BuildTools(targets);

        // A malformed or missing tool call (no_tool_call, an unknown
        // action, a bad target) is model flakiness on that one attempt,
        // not a real error — worth one immediate retry, reusing the
        // same thought, before giving up on the turn. A genuine
        // provider/network failure is different (retrying instantly
        // won't fix a dead connection), so that bails straight away and
        // lets the caller's own backoff handle it.
        const int maxActAttempts = 2;
        ChatResult actResult = default;
        ParseResult parsed = default;
        for (int attempt = 1; attempt <= maxActAttempts; attempt++)
        {
            actResult = await _provider.Chat(actMessages, tools, temperature);
            if (!actResult.Ok)
                return MindResult.Fail($"act_{actResult.Error}", thought);

            parsed = ParseToolCall(actResult.Message, targets);
            if (parsed.Ok)
                return MindResult.Success(thought, parsed.Action);
        }

        return MindResult.Fail(parsed.Error, thought);
    }

    public readonly struct ThreatResult
    {
        public readonly bool Ok;
        public readonly string Error;
        public readonly string Choice; // "fight" | "flee" | "freeze" — only meaningful when Ok

        private ThreatResult(bool ok, string error, string choice)
        {
            Ok = ok;
            Error = error;
            Choice = choice;
        }

        public static ThreatResult Fail(string error) => new(false, error, null);
        public static ThreatResult Success(string choice) => new(true, null, choice);
    }

    // The fight/flee/freeze decision — genuinely serviced to the LLM
    // first, per "try to service these options to LLM first," before
    // NpcAgent ever falls back to a random, stat-weighted choice of
    // its own. Deliberately its own method rather than a special case
    // of Decide()/BuildTools() above: the tool list here is fixed at
    // exactly three entries, none of which are "real" GameAction ids
    // (NpcAgent.MapThreatChoiceToAction turns the choice into an
    // actual attack/flee/wait afterward) so there's nothing here for
    // ParseToolCall's normal target-validation trust boundary to do.
    public async Task<ThreatResult> DecideThreatResponse(string perceptionText, Personality personality, bool selfTargeted)
    {
        string persona = personality.DescribeForPrompt();
        float temperature = personality.Temperature;
        string instruction = selfTargeted ? ThreatInstruction : AllyThreatInstruction;

        var messages = new object[]
        {
            new { role = "system", content = $"{persona}\n\n{instruction}" },
            new { role = "user", content = perceptionText },
        };
        object[] tools = BuildThreatTools();

        ChatResult result = await _provider.Chat(messages, tools, temperature);
        if (!result.Ok)
            return ThreatResult.Fail($"act_{result.Error}");

        if (result.Message.ToolCalls is not { Length: > 0 })
            return ThreatResult.Fail("no_tool_call");

        FunctionCall fn = result.Message.ToolCalls[0]?.Function;
        if (fn == null || string.IsNullOrEmpty(fn.Name))
            return ThreatResult.Fail("malformed_tool_call");

        string choice = fn.Name;
        return choice is "fight" or "flee" or "freeze"
            ? ThreatResult.Success(choice)
            : ThreatResult.Fail($"unknown_choice_{choice}");
    }

    // The direct-request counterpart to DecideThreatResponse above —
    // same "single round trip, no think-then-act split" shape, called
    // instead of Decide() whenever the player spoke to this NPC this
    // turn (see NpcAgent.HandleDirectPlayerRequest) — but reuses
    // Decide()'s own BuildTools()/ParseToolCall() rather than a fixed
    // three-option schema like DecideThreatResponse's: "agree" isn't
    // one fixed action here, it's whichever real tool actually matches
    // what the player asked for (follow, catch_fish, pick_apple, ...),
    // and "decline" is just speak, already one of those same tools.
    // Same bounded-retry posture as Decide()'s own act call — a
    // malformed or off-menu tool call is one retry before giving up on
    // the turn, not a hard failure.
    public async Task<MindResult> DecidePlayerRequest(string perceptionText, AvailableTargets targets, Personality personality)
    {
        string persona = personality.DescribeForPrompt();
        float temperature = personality.Temperature;

        var messages = new object[]
        {
            new { role = "system", content = $"{persona}\n\n{PlayerRequestInstruction}" },
            new { role = "user", content = perceptionText },
        };
        object[] tools = BuildTools(targets);

        const int maxAttempts = 2;
        ChatResult result = default;
        ParseResult parsed = default;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            result = await _provider.Chat(messages, tools, temperature);
            if (!result.Ok)
                return MindResult.Fail($"act_{result.Error}");

            parsed = ParseToolCall(result.Message, targets);
            if (parsed.Ok)
                return MindResult.Success("", parsed.Action);
        }

        return MindResult.Fail(parsed.Error);
    }

    private static object[] BuildThreatTools()
    {
        // No target_id/item/anything on any of these — WHICH animal to
        // fight or which direction to flee is a mechanical decision
        // NpcAgent makes itself (nearest threat; straight away from
        // it), not something offered to the model. That keeps this
        // schema genuinely limited to the three verbs the user asked
        // for, not three-verbs-times-however-many-targets.
        return new object[]
        {
            new
            {
                type = "function",
                function = new
                {
                    name = "fight",
                    description = "Fight — either defend yourself against whatever is attacking you, or go help a friend who's being attacked, whichever actually applies right now.",
                    parameters = new { type = "object", properties = new { } },
                },
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "flee",
                    description = "Run — get away from the danger, right now, whether it's after you or after someone else.",
                    parameters = new { type = "object", properties = new { } },
                },
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "freeze",
                    description = "Freeze — hold still and do nothing, whether that means calling for help yourself or just watching someone else's fight.",
                    parameters = new { type = "object", properties = new { } },
                },
            },
        };
    }

    // Called by NpcMemory once its raw log outgrows MaxLlmChars. Same
    // "no exceptions to lean on" posture as Decide(): if the compression
    // call itself fails, fall back to a naive truncation rather than
    // lose the memory or crash — a worse summary is better than none.
    public async Task<string> Summarize(string existingDiary, string rawLog)
    {
        string prompt = string.IsNullOrEmpty(existingDiary)
            ? rawLog
            : $"Existing diary: {existingDiary}\n\nNew events since then:\n{rawLog}";

        var messages = new object[]
        {
            new { role = "system", content = SummarizeSystemPrompt },
            new { role = "user", content = prompt },
        };

        ChatResult result = await _provider.Chat(messages, null);
        if (!result.Ok)
            return TruncateFallback(existingDiary, rawLog);

        string summary = (result.Message.Content ?? "").Trim();
        return string.IsNullOrEmpty(summary) ? TruncateFallback(existingDiary, rawLog) : summary;
    }

    private static string TruncateFallback(string existingDiary, string rawLog)
    {
        string combined = string.IsNullOrEmpty(existingDiary) ? rawLog : $"{existingDiary} {rawLog}";
        const int keep = 400;
        return combined.Length > keep ? "..." + combined.Substring(combined.Length - keep) : combined;
    }

    private readonly struct ParseResult
    {
        public readonly bool Ok;
        public readonly string Error;
        public readonly GameAction Action;

        private ParseResult(bool ok, string error, GameAction action)
        {
            Ok = ok;
            Error = error;
            Action = action;
        }

        public static ParseResult Fail(string error) => new(false, error, null);
        public static ParseResult Success(GameAction action) => new(true, null, action);
    }

    // The single trust boundary between whatever the provider sent back
    // and a GameAction the engine is allowed to act on. An unrecognized
    // action name or a target that isn't in the world right now is
    // rejected here, explicitly, rather than assumed valid.
    private ParseResult ParseToolCall(ChatMessage message, AvailableTargets targets)
    {
        if (message.ToolCalls is not { Length: > 0 })
            return ParseResult.Fail("no_tool_call");

        FunctionCall fn = message.ToolCalls[0]?.Function;
        if (fn == null || string.IsNullOrEmpty(fn.Name))
            return ParseResult.Fail("malformed_tool_call");

        string name = fn.Name;
        if (Array.IndexOf(ValidActions, name) < 0)
            return ParseResult.Fail($"unknown_action_{name}");

        string targetId = ExtractField(fn.Arguments, "target_id");
        Emotion emotion = EmotionExtensions.Parse(ExtractField(fn.Arguments, "emotion"));

        switch (name)
        {
            case "pick_apple":
                if (Array.IndexOf(targets.TreeIds, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.PickApple, emotion));
            case "catch_fish":
                if (Array.IndexOf(targets.FishingSpotIds, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.CatchFish, emotion));
            case "gather_pinecone":
                if (Array.IndexOf(targets.PineTreeIds, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.GatherPinecone, emotion));
            case "gather_berry":
                if (Array.IndexOf(targets.BerryBushIds, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.GatherBerry, emotion));
            case "deposit":
                // Only one valid target exists for this tool — a small
                // model sometimes omits an enum-of-one argument even
                // though it's required. Default rather than fail on
                // that specific, unambiguous case; still reject any
                // other explicit (wrong) value.
                if (targetId == "")
                    targetId = "home";
                if (targetId != "home")
                    return ParseResult.Fail($"invalid_target_{targetId}");
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.Deposit, emotion));
            case "travel":
                if (Array.IndexOf(targets.TravelTargetIds, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.Travel, emotion));
            case "speak":
                // Named spokenMessage, not message — that name is
                // already the ChatMessage parameter to this whole
                // method, and C# doesn't allow shadowing it here.
                string spokenMessage = ExtractField(fn.Arguments, "message");
                if (string.IsNullOrWhiteSpace(spokenMessage))
                    return ParseResult.Fail("empty_message");
                return ParseResult.Success(new GameAction(name, "", 0f, emotion, spokenMessage));
            case "follow":
                if (Array.IndexOf(targets.NearbyNpcNames, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.Follow, emotion));
            case "trade":
            {
                if (Array.IndexOf(targets.NearbyNpcNames, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                string item = ExtractField(fn.Arguments, "item");
                // Can only ever offer what you actually have — the tool
                // schema already restricts the enum to carriedItems, but
                // a small model can still miss the enum, so this is the
                // real trust boundary, not just the schema.
                if (Array.IndexOf(targets.CarriedItems, item) < 0)
                    return ParseResult.Fail($"invalid_item_{item}");
                int amount = ParseAmount(fn.Arguments);
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.Trade, emotion, item: item, amount: amount));
            }
            case "steal":
            {
                if (Array.IndexOf(targets.NearbyNpcNames, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                string item = ExtractField(fn.Arguments, "item");
                if (Array.IndexOf(ItemTypes, item) < 0)
                    return ParseResult.Fail($"invalid_item_{item}");
                int amount = ParseAmount(fn.Arguments);
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.Steal, emotion, item: item, amount: amount));
            }
            case "attack":
                if (Array.IndexOf(targets.AnimalIds, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.Attack, emotion));
            case "eat":
                // The tool schema already omits "eat" when !EatAllowed
                // (see BuildTools) — same "schema is a hint, this is
                // the real trust boundary" posture as sleep below.
                if (!targets.EatAllowed)
                    return ParseResult.Fail("eat_not_available");
                return ParseResult.Success(new GameAction(name, "", 0f, emotion));
            case "pick_up_stick":
                if (Array.IndexOf(targets.StickIds, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.PickUpStick, emotion));
            case "sleep":
                // The tool schema already omits "sleep" entirely when
                // !sleepAllowed (see BuildTools), so this only ever
                // catches a model calling it anyway — the same
                // "schema is a hint, this is the real trust boundary"
                // posture every other case here already has.
                if (!targets.SleepAllowed)
                    return ParseResult.Fail("sleep_not_available");
                return ParseResult.Success(new GameAction(name, "", 0f, emotion));
            case "light_fire":
                if (!targets.LightFireAllowed)
                    return ParseResult.Fail("light_fire_not_available");
                return ParseResult.Success(new GameAction(name, "firepit", ActionRanges.FirePit, emotion));
            case "make_torch":
                if (!targets.MakeTorchAllowed)
                    return ParseResult.Fail("make_torch_not_available");
                return ParseResult.Success(new GameAction(name, "firepit", ActionRanges.FirePit, emotion));
            case "cook_meat":
                if (!targets.CookMeatAllowed)
                    return ParseResult.Fail("cook_meat_not_available");
                return ParseResult.Success(new GameAction(name, "firepit", ActionRanges.FirePit, emotion));
            default: // "wait"
                return ParseResult.Success(new GameAction("wait", "", 0f, emotion));
        }
    }

    // Small models (local or cloud) don't always fill a schema exactly
    // — seen wrapping a single expected string in a one-element array.
    // Handle both shapes rather than trusting the schema was followed.
    private static string ExtractField(System.Text.Json.JsonElement arguments, string field)
    {
        if (arguments.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !arguments.TryGetProperty(field, out var value))
            return "";
        return CoerceToString(value);
    }

    // "amount" is optional in the schema (defaults to 1) — a small model
    // omitting it entirely, sending it as a string, or sending 0/negative
    // are all treated as "just one," rather than rejecting the whole
    // call over an incidental formatting slip.
    private static int ParseAmount(System.Text.Json.JsonElement arguments)
    {
        string raw = ExtractField(arguments, "amount");
        return int.TryParse(raw, out int amount) && amount > 0 ? amount : 1;
    }

    private static string CoerceToString(System.Text.Json.JsonElement value)
    {
        return value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => value.GetString() ?? "",
            System.Text.Json.JsonValueKind.Array => value.GetArrayLength() > 0 ? CoerceToString(value[0]) : "",
            _ => value.ToString(),
        };
    }

    private static object[] BuildTools(AvailableTargets targets)
    {
        var tools = new System.Collections.Generic.List<object>
        {
            new
            {
                type = "function",
                function = new
                {
                    name = "pick_apple",
                    description = "Walk to an apple tree and pick an apple from it.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            target_id = new { type = "string", @enum = targets.TreeIds, description = "which tree to pick from" },
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "target_id", "emotion" },
                    },
                },
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "catch_fish",
                    description = "Walk to a spot along the river and try to catch a fish there.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            target_id = new { type = "string", @enum = targets.FishingSpotIds, description = "which fishing spot to try" },
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "target_id", "emotion" },
                    },
                },
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "deposit",
                    description = "Walk home and deposit everything you're currently carrying, whatever the mix of items.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            target_id = new { type = "string", @enum = new[] { "home" } },
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "target_id", "emotion" },
                    },
                },
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "wait",
                    description = "Do nothing this turn.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "emotion" },
                    },
                },
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "speak",
                    description = "Say something out loud, in your own words — a greeting, a story, an argument, a pitch trying to talk someone into something, anything. Anyone within hearing range right now may hear it and decide how to react on their own, later, including being persuaded if what you say actually lands with them — this does not control or compel anyone, and if nobody's around, nobody hears it.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            message = new { type = "string", description = "what you say out loud" },
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "message", "emotion" },
                    },
                },
            },
        };

        // Only offered when NPCActor.CanSleep() says the conditions are
        // actually met right now (see its comment for the three-tier
        // rule: never above SleepUnnecessaryThreshold, always below
        // LowFatigueThreshold, otherwise only near home) — same
        // "conditionally offered, not just always there" treatment as
        // gather_pinecone/gather_berry/travel/follow/trade/steal below.
        if (targets.SleepAllowed)
        {
            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "sleep",
                    description = "Rest right where you are and fully restore your fatigue. Takes a while — a real choice for when you're actually tired, not routine.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "emotion" },
                    },
                },
            });
        }

        // Only offered when there's actually a pine tree to gather
        // from — an enum-of-nothing tool can never be called validly,
        // so omit it rather than offer a dead option. Unlike pick_apple/
        // catch_fish (hand-placed and guaranteed to exist from the
        // start), pine trees can be purely exploration-generated in
        // principle, so this can't assume the enum is never empty the
        // way those two do.
        if (targets.PineTreeIds.Length > 0)
        {
            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "gather_pinecone",
                    description = "Walk to a pine tree and gather a pinecone from it.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            target_id = new { type = "string", @enum = targets.PineTreeIds, description = "which pine tree to gather from" },
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "target_id", "emotion" },
                    },
                },
            });
        }

        // Same reasoning as gather_pinecone above.
        if (targets.BerryBushIds.Length > 0)
        {
            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "gather_berry",
                    description = "Walk to a berry bush and pick a berry from it — whichever kind that particular bush actually grows.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            target_id = new { type = "string", @enum = targets.BerryBushIds, description = "which berry bush to gather from" },
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "target_id", "emotion" },
                    },
                },
            });
        }

        // Only offered when there's actually a flagpole to name — an
        // enum-of-nothing tool can never be called validly, so omit it
        // rather than offer a dead option.
        if (targets.TravelTargetIds.Length > 0)
        {
            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "travel",
                    description = "Walk toward a distant landmark out of curiosity, not to gather anything — you may not know the way there yet.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            target_id = new { type = "string", @enum = targets.TravelTargetIds, description = "which distant landmark to head toward" },
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "target_id", "emotion" },
                    },
                },
            });
        }

        // Same reasoning as travel — offered only when there's actually
        // someone nameable nearby to follow, so the enum is never empty.
        if (targets.NearbyNpcNames.Length > 0)
        {
            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "follow",
                    description = "Walk alongside someone nearby, by name — your own choice, re-decided fresh every turn, not a commitment.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            target_id = new { type = "string", @enum = targets.NearbyNpcNames, description = "the name of who to follow" },
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "target_id", "emotion" },
                    },
                },
            });
        }

        // Same reasoning as travel/follow — offered only when there's
        // actually someone nearby AND something in hand to offer them,
        // so the item enum is never empty either.
        if (targets.NearbyNpcNames.Length > 0 && targets.CarriedItems.Length > 0)
        {
            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "trade",
                    description = "Give someone nearby an item you're carrying — a real choice about generosity or self-interest.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            target_id = new { type = "string", @enum = targets.NearbyNpcNames, description = "who to give it to" },
                            item = new { type = "string", @enum = targets.CarriedItems, description = "which item to give — only what you're actually carrying" },
                            amount = new { type = "integer", description = "how many to give (defaults to 1)" },
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "target_id", "item", "emotion" },
                    },
                },
            });
        }

        // steal doesn't require carrying anything yourself, only someone
        // nearby to target — you're guessing at what they have, same as
        // the description says; a wrong guess just fails cleanly.
        if (targets.NearbyNpcNames.Length > 0)
        {
            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "steal",
                    description = "Take an item from someone nearby without asking. You don't know for certain what they're carrying — it's a guess, and it fails harmlessly if they don't have it. Not hidden from them forever: they keep their own count and may notice something's missing later.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            target_id = new { type = "string", @enum = targets.NearbyNpcNames, description = "who to take it from" },
                            item = new { type = "string", @enum = ItemTypes, description = "which item to try to take" },
                            amount = new { type = "integer", description = "how many to try to take (defaults to 1)" },
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "target_id", "item", "emotion" },
                    },
                },
            });
        }

        // Only offered when there's actually something nearby worth
        // fighting — a wild animal within range. attack against
        // another person isn't offered at all right now (nothing here
        // decides that's ever a real option) — this is strictly the
        // "an animal is a threat, or worth taking on" case.
        if (targets.AnimalIds.Length > 0)
        {
            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "attack",
                    description = "Fight a nearby wild animal — walk up and strike it, with whatever weapon (or bare hands) you're carrying. Can miss, and it can hit back.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            target_id = new { type = "string", @enum = targets.AnimalIds, description = "which animal to attack" },
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "target_id", "emotion" },
                    },
                },
            });
        }

        // Only offered once NPCActor.CanEat() says it's a real option —
        // Health actually below the threshold, and real food on hand.
        if (targets.EatAllowed)
        {
            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "eat",
                    description = "Eat something from what you're carrying to restore some Health. Uses whichever food you have.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "emotion" },
                    },
                },
            });
        }

        // Only offered when there's actually a stick on the ground
        // nearby.
        if (targets.StickIds.Length > 0)
        {
            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "pick_up_stick",
                    description = "Pick up a stick lying on the ground — a basic weapon, better than bare hands in a fight.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            target_id = new { type = "string", @enum = targets.StickIds, description = "which stick to pick up" },
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "target_id", "emotion" },
                    },
                },
            });
        }

        // Only offered while the fire pit is actually unlit — see
        // FirePit's own header for the 5-minute duration.
        if (targets.LightFireAllowed)
        {
            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "light_fire",
                    description = "Walk to the fire pit near home and light it. Stays lit for a while, then goes out on its own.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "emotion" },
                    },
                },
            });
        }

        // Only offered while the fire pit IS lit and there's actually
        // a stick on hand to light from it.
        if (targets.MakeTorchAllowed)
        {
            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "make_torch",
                    description = "Light a stick you're carrying from the burning fire pit, turning it into a torch — a personal light source. Burns out and turns back into a plain stick after a while; needs the fire pit lit again to relight.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "emotion" },
                    },
                },
            });
        }

        // Only offered while the fire pit IS lit and there's actual raw
        // rabbit_meat on hand — see Food.cs's own header for why
        // cooked_meat is worth the trouble (the most filling food in
        // the game, and raw meat isn't edible at all otherwise).
        if (targets.CookMeatAllowed)
        {
            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "cook_meat",
                    description = "Cook raw rabbit meat you're carrying over the burning fire pit, turning it into cooked meat — restores far more health and hunger than raw meat (which isn't edible at all) when you eat it later, anytime, anywhere.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "emotion" },
                    },
                },
            });
        }

        return tools.ToArray();
    }
}
