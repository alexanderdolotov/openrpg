using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    // Rewritten clean/minimal, 2026-09-08 — the dense, exhaustively-
    // reasoned paragraph this used to be (every tool explained at
    // length, edge cases spelled out inline) is preserved in git
    // history. llm_tuning's baseline eval (live against Ollama, not
    // simulated) measured this model doing BETTER on the tool-confusion
    // it was originally trying to prevent (pick_apple vs
    // gather_pinecone) once the prose shrank and BuildTools' own
    // conditional gating (see its own comments) started doing more of
    // the actual constraining — a long instruction can't out-argue a
    // tool that simply isn't in the list. See llm_tuning/common.py's
    // ACT_INSTRUCTION (the version actually benchmarked) — port any
    // future change there too, in the same commit.
    private const string ActInstruction =
        "Call exactly one of the tools listed below — whichever one best fits your personality, stats, and the situation above right now. If your last action just failed, don't repeat it — pick something that addresses why. Only target an id that's explicitly listed above; never invent one.";
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
    // (Decide() above) and swapping in this instruction on its own,
    // called only for the one turn the player is actually waiting on an
    // answer (see NpcAgent.HandleDirectPlayerRequest).
    //
    // Rewritten clean/minimal, 2026-09-08 — same reasoning as
    // ActInstruction's own header just above: the original ran to
    // ~3.5KB of exhaustively-reasoned prose (every hedge/musing/dodge
    // pattern spelled out at length), preserved in git history.
    // llm_tuning's baseline eval measured no real regression from
    // shrinking it — see llm_tuning/common.py's PLAYER_REQUEST_INSTRUCTION
    // (the version actually benchmarked, including the "come with me"
    // follow-phrasing clause and the compound question+request rule
    // below) — port any future change there too, in the same commit.
    // 2026-09-09: added a concrete, named example to the "tool not
    // listed" clause after a real prompt_debug session (live gameplay,
    // the deployed fine-tuned model, not eval-only) caught this exact
    // substitution twice in one session — asked to catch fish with no
    // catch_fish tool offered (no fishing spot in sight), Finn called
    // travel and Maren called gather_berry instead of declining either
    // time. This is the single weak spot that survived every prompt
    // change tried this whole project (see llm_tuning/README.md's
    // "Results so far") — the generic "or call speak... if the tool for
    // it isn't listed" framing clearly isn't concrete enough on its own.
    // Not expected to fully fix it (nothing has), but worth the cheap
    // try before concluding this needs the fine-tune to carry the rest.
    //
    // 2026-09-10: added the backstory-grounding + collaborative-default
    // paragraph after the SAME session's prompt_debug log caught a
    // different, real problem — asked to pick apples, Finn (BACKGROUND:
    // "...he's never turned down a good apple either") declined with
    // "Not my thing," directly contradicting his own stated backstory.
    // pick_apple WAS genuinely offered (confirmed in the log — this
    // wasn't the not_available case above), so nothing here stopped the
    // model from inventing an out-of-character excuse rather than
    // either helping or declining for a REAL reason. Maren declining the
    // same request ("I'd like some quiet time") was fine — genuinely
    // consistent with her own backstory and personality — so the fix
    // isn't "decline less," it's "any decline has to actually be true of
    // this character," plus a real, if soft, nudge that reflexive
    // refusal without a specific reason isn't personality, it's just
    // unhelpful. Deliberately NOT "always say yes" — Bram (low
    // agreeableness, guarded) should still say no more often than Maren
    // or Finn; that contrast is the actual personality variety this
    // project wants, not something to flatten.
    private const string PlayerRequestInstruction =
        "The player just spoke to you directly — see HEARD above. Answer them this turn with a real tool call, not just words. If it's a question, call speak with the honest, specific answer from what's listed above. If it's a request to do something, call the one matching tool right now if you're willing — including a casual \"come with me\"/\"walk with me\"/\"stay with me\" (that's follow, even without the word \"follow\" in it) — or call speak to say no, plainly, if you're not willing, or if the tool for it isn't listed below at all right now. That last case is a real, ordinary \"can't,\" and it comes up constantly: asked to pick apples with no pick_apple tool below, or catch fish with no catch_fish tool below, that specifically means no apple tree or fishing spot is close enough to see right now — say so plainly (\"I don't see any apple trees near me\" / \"there's no fishing spot in sight\") rather than reaching for a different gathering tool (gather_berry, gather_pinecone, pick_up_stick) or traveling off on your own guess instead — none of those are an answer to what was actually asked, just a way of quietly not answering it. Whatever reason you give, for going along with it or declining, has to actually be true of you — check it against your own BACKGROUND and PERSONALITY above before you say it, and never invent a reason that contradicts a fact already stated there. Default toward helping when you reasonably can: a genuine personality trait, an honest mood, or a real practical reason are all legitimate reasons to say no, but declining just because you can, with no real reason behind it, isn't personality, it's just unhelpful — some of you are more guarded than others, and that's fine, but it should come from who you actually are, not from reaching for an excuse. If they said both a question AND a request in the same line, answer the request — it's the time-sensitive half; the fact they asked about is still just as true and still answerable next time they ask.";

    private const string SummarizeSystemPrompt =
        "You are compressing an NPC's memory log into a short diary paragraph (3-5 sentences) they'll carry forward. Preserve what matters for future decisions — where they've been, what they've done, anything notable, and anything said aloud (by them or heard from someone else), including who said or asked for what by name. A repeated identical failure is NOT routine detail — it's the opposite: state plainly what failed, why, and how many times, so it isn't attempted again pointlessly. Drop only genuinely routine, non-repeated detail (a single successful wait, a normal walk). Write in first person, past tense. Output ONLY the diary paragraph itself — no preamble like \"Here's my attempt to condense this...\", no closing note explaining what you kept or why. The reader is the NPC remembering their own day, not someone reviewing your summarization work.";

    private static readonly string[] ValidActions = { "pick_apple", "catch_fish", "gather_pinecone", "gather_berry", "deposit", "travel", "speak", "follow", "trade", "steal", "attack", "eat", "pick_up_stick", "sleep", "wait", "light_fire", "make_torch", "cook_meat" };

    // Every item type that currently exists in the world — trade/steal
    // both need a fixed, enumerable answer to "which item" for the tool
    // schema. Grows the day a new resource type does, same as
    // ActionRanges already does per-action. Internal, not private —
    // referenced elsewhere for the same "which item" question, though
    // PlayerCharacter's own steal button no longer rolls uniformly from
    // this full list (see its own comment): it draws from the target's
    // actual carried items instead, so a wrong-category guess can't
    // fail a steal against someone who was never carrying that item to
    // begin with.
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

    public async Task<MindResult> Decide(string perceptionText, AvailableTargets targets, Personality personality, string statsLine)
    {
        string persona = $"{personality.DescribeForPrompt()}\nSTATS: {statsLine}";
        float temperature = personality.Temperature;

        var thinkMessages = new object[]
        {
            new { role = "system", content = $"{persona}\n\n{ThinkInstruction}" },
            new { role = "user", content = perceptionText },
        };
        ChatResult thinkResult = await _provider.Chat(thinkMessages, null, temperature);
        PromptDebugLogger.Log(personality.Name, "think", $"{persona}\n\n{ThinkInstruction}", perceptionText, Array.Empty<string>(), thinkResult);
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
            PromptDebugLogger.Log(personality.Name, "act", $"{persona}\n\n{ActInstruction}", $"{perceptionText}\n\nYour plan: {thought}", ToolNames(tools), actResult);
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
        PromptDebugLogger.Log(personality.Name, selfTargeted ? "threat_self" : "threat_ally", $"{persona}\n\n{instruction}", perceptionText, ToolNames(tools), result);
        if (!result.Ok)
            return ThreatResult.Fail($"act_{result.Error}");

        if (result.Message.ToolCalls is { Length: > 0 })
        {
            FunctionCall fn = result.Message.ToolCalls[0]?.Function;
            if (fn == null || string.IsNullOrEmpty(fn.Name))
                return ThreatResult.Fail("malformed_tool_call");

            string choice = fn.Name;
            return choice is "fight" or "flee" or "freeze"
                ? ThreatResult.Success(choice)
                : ThreatResult.Fail($"unknown_choice_{choice}");
        }

        // Same "be generous — use some regex to see if it's TRYING to
        // call a tool" leniency as ParseToolCall's own LenientParseFromText
        // below, applied to this much narrower three-word schema: no
        // structured call at all, but "I'll fight" or just "flee" sitting
        // in the plain text content is still a real, recoverable choice,
        // not a genuine failure. MatchesIntent (shared with
        // LenientParseFromText) is what keeps "I will NOT flee" from
        // being misread as choosing flee.
        string content = result.Message.Content ?? "";
        string lenientChoice = new[] { "fight", "flee", "freeze" }
            .FirstOrDefault(c => MatchesIntent(content, c));
        return lenientChoice != null
            ? ThreatResult.Success(lenientChoice)
            : ThreatResult.Fail("no_tool_call");
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
    public async Task<MindResult> DecidePlayerRequest(string perceptionText, AvailableTargets targets, Personality personality, string statsLine)
    {
        string persona = $"{personality.DescribeForPrompt()}\nSTATS: {statsLine}";
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
            PromptDebugLogger.Log(personality.Name, "player_request", $"{persona}\n\n{PlayerRequestInstruction}", perceptionText, ToolNames(tools), result);
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
    // SummarizeSystemPrompt only ASKS for "3-5 sentences" — a soft
    // instruction the real logs showed a small model doesn't always
    // honor (some compressed diary paragraphs ran well past that). Since
    // Diary feeds straight into every future prompt's fixed-ish overhead
    // (see MindConfig.NumCtx's own comment on the context-window budget),
    // an occasional overlong summary isn't just untidy, it's exactly the
    // kind of slow, unbounded growth that eventually crowds out the
    // actual situation and any direct request in it. A hard backstop
    // here, same "keep the most recent tail" shape as TruncateFallback
    // below, closes that gap regardless of how well any given
    // summarization call behaves.
    private const int MaxDiaryChars = 800;

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
        if (string.IsNullOrEmpty(summary))
            return TruncateFallback(existingDiary, rawLog);
        return summary.Length > MaxDiaryChars ? "..." + TrimToTail(summary, MaxDiaryChars) : summary;
    }

    private static string TruncateFallback(string existingDiary, string rawLog)
    {
        string combined = string.IsNullOrEmpty(existingDiary) ? rawLog : $"{existingDiary} {rawLog}";
        const int keep = 400;
        return combined.Length > keep ? "..." + TrimToTail(combined, keep) : combined;
    }

    // A plain Substring(s.Length - maxChars) can land exactly on the
    // low half of a surrogate pair (an emoji, some CJK extension
    // characters — rare from a local LLM's English output, but not
    // impossible) and hand back a lone, invalid surrogate at the start
    // of the result. Nudging the cut forward one more character when
    // that happens keeps the tail on a real character boundary instead.
    private static string TrimToTail(string s, int maxChars)
    {
        int start = s.Length - maxChars;
        if (start > 0 && char.IsLowSurrogate(s[start]))
            start++;
        return s.Substring(start);
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
        if (message.ToolCalls is { Length: > 0 })
        {
            FunctionCall fn = message.ToolCalls[0]?.Function;
            if (fn == null || string.IsNullOrEmpty(fn.Name))
                return ParseResult.Fail("malformed_tool_call");

            string name = fn.Name;
            if (Array.IndexOf(ValidActions, name) < 0)
                return ParseResult.Fail($"unknown_action_{name}");

            string targetId = ExtractField(fn.Arguments, "target_id");
            Emotion emotion = EmotionExtensions.Parse(ExtractField(fn.Arguments, "emotion"));
            string item = ExtractField(fn.Arguments, "item");
            int amount = ParseAmount(fn.Arguments);
            string spokenMessage = ExtractField(fn.Arguments, "message");
            return BuildAction(name, targetId, emotion, item, amount, spokenMessage, targets);
        }

        // "Be generous with these small LLMs trying to call tools — use
        // some generous regex to see if they're TRYING to call a tool,
        // to go along with this game easier." A real, observed failure
        // mode (see the FALLBACK_DISABLED/no_tool_call lines a 3B local
        // model produces fairly often): rather than using the
        // provider's real function-calling format, it just narrates
        // the action in plain prose instead — "I'll light_fire" or
        // "action: pick_apple tree_2" as ordinary text content, with no
        // ToolCalls at all. The model's INTENT is still right there in
        // that text, so this takes one best-effort, deliberately loose
        // pass at recovering a real action from it before giving up —
        // see LenientParseFromText's own header for how loose, and why
        // that's still safe.
        ParseResult lenient = LenientParseFromText(message.Content, targets);
        if (lenient.Ok) return lenient;

        return ParseResult.Fail("no_tool_call");
    }

    // Scans the model's own plain-text reply for the name of one of
    // ValidActions and, best-effort, a target/item id it's already
    // allowed to use — no real NLU, just "is one of our exact action
    // names sitting in this text as a whole word, and does the text
    // separately happen to contain one of the ids/items actually on
    // offer right now." Deliberately generous rather than exact: this
    // is reached ONLY when there was no structured tool call at all
    // (see ParseToolCall above), so there is nothing more literal left
    // to try, and a wrong guess here is still caught by BuildAction's
    // own target/item validation below (the same real trust boundary
    // the structured path already goes through) — this can recover a
    // genuine intent, but it can never make an invalid action succeed.
    // Longest-name-first ordering throughout avoids a short id/action
    // name winning a spurious match against a longer one that also
    // appears (e.g. "tree_1" inside "tree_10").
    //
    // Two guards keep "generous" from tipping into "wrong": MatchesIntent
    // below rejects a match with a negation word ("not"/"never"/"don't"/
    // ...) sitting immediately before it — bare keyword presence alone
    // can't tell "I will attack" from "I will NOT attack", and short,
    // common action words (eat/wait/sleep/speak/trade/follow) genuinely
    // do turn up in ordinary reflective prose that isn't stating a real
    // decision at all. And "speak" is tried LAST, only once nothing more
    // specific matched — it's the vaguest possible category (almost any
    // reply "is" speech in some sense), so letting it win on word length
    // alone would too easily steal the turn from a real, more specific
    // intent stated elsewhere in the same text; speak also has no
    // target/item validation net at all in BuildAction, so a bad match
    // there just becomes SOME line of in-character dialogue with no
    // second check to catch it.
    private ParseResult LenientParseFromText(string content, AvailableTargets targets)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ParseResult.Fail("no_tool_call");

        string name = ValidActions
            .Where(a => a != "speak")
            .OrderByDescending(a => a.Length)
            .FirstOrDefault(a => MatchesIntent(content, a));
        if (name == null && MatchesIntent(content, "speak"))
            name = "speak";
        if (name == null)
            return ParseResult.Fail("no_tool_call");

        string targetId = FindMentionedId(content, AllKnownTargetIds(targets));
        string item = FindMentionedId(content, ItemTypes);
        // No attempt at a real amount/emotion regex here — a model
        // that skipped structured tool-calling entirely is unlikely to
        // state either reliably in prose, and both already have sane
        // defaults (ParseAmount's "just one" for amount, Parse("")'s
        // own default for emotion) that the structured path itself
        // falls back on for exactly the same reason.
        Emotion emotion = EmotionExtensions.Parse("");
        // speak has no real target/item concept — if this IS speak,
        // the "spoken message" is simply whatever the model actually
        // wrote, since that's genuinely what it's trying to say when
        // it's answering in plain prose to begin with.
        string spokenMessage = name == "speak" ? content.Trim() : "";

        return BuildAction(name, targetId, emotion, item, 1, spokenMessage, targets);
    }

    // Natural-language trigger phrases per action, used ONLY by
    // TryRecoverCommittedAction below — unlike LenientParseFromText's
    // own scan above (which matches the LITERAL snake_case tool name,
    // reasonable there since a model that skipped structured tool-
    // calling entirely often echoes back the exact identifier it just
    // saw in its own tool schema), a SPOKEN, in-character reply is
    // ordinary English, not tool-schema vocabulary — nobody actually
    // says "make_torch" out loud. Reusing LenientParseFromText's own
    // snake_case scan here (an earlier pass at this genuinely did
    // exactly that) would silently never fire on real dialogue at all,
    // which defeats the entire point. Scoped to the handful of actions
    // most likely to actually come up mid-conversation, not an
    // exhaustive map for all eighteen — everything else simply isn't
    // recovered from a hedgy speak reply, which just means the speak
    // stands as spoken, same as before this existed.
    // pick_up_stick/pick_apple/catch_fish/gather_pinecone/gather_berry
    // got a much wider phrase net than the original handful here — a
    // real, observed failure ("Finn, go find a stick" / "Maren go find
    // some sticks" never recovered, across several rephrasings, because
    // NONE of them said the one exact phrase "pick up the stick"/"grab
    // the stick" this list originally had). Now that BuildAction can
    // actually resolve one of these five to a real nearby target
    // without the text ever naming which one (see its own header),
    // the phrase-matching side needed to stop being the narrower
    // bottleneck — these are asked-for constantly and phrased a dozen
    // different ordinary ways, unlike make_torch/light_fire/cook_meat/
    // follow/attack/eat/sleep, which stay closer to their original,
    // narrower set.
    private static readonly (string Action, string Phrase)[] SpokenIntentPhrases =
    {
        ("make_torch", "make a torch"), ("make_torch", "light a torch"), ("make_torch", "craft a torch"),
        ("light_fire", "light the fire"), ("light_fire", "light it"), ("light_fire", "start the fire"),
        ("cook_meat", "cook the meat"), ("cook_meat", "cook it"), ("cook_meat", "cook some meat"),
        ("follow", "follow you"), ("follow", "come with you"),
        ("pick_up_stick", "pick up the stick"), ("pick_up_stick", "pick up a stick"), ("pick_up_stick", "grab the stick"), ("pick_up_stick", "grab a stick"),
        ("pick_up_stick", "find a stick"), ("pick_up_stick", "find some sticks"), ("pick_up_stick", "find sticks"),
        ("pick_up_stick", "get a stick"), ("pick_up_stick", "get some sticks"), ("pick_up_stick", "get sticks"),
        ("pick_up_stick", "look for a stick"), ("pick_up_stick", "look for sticks"),
        ("pick_up_stick", "collect a stick"), ("pick_up_stick", "collect some sticks"), ("pick_up_stick", "collect sticks"),
        ("pick_up_stick", "gather a stick"), ("pick_up_stick", "gather some sticks"), ("pick_up_stick", "gather sticks"),
        ("pick_apple", "pick an apple"), ("pick_apple", "pick some apples"), ("pick_apple", "get an apple"), ("pick_apple", "get some apples"),
        ("pick_apple", "grab an apple"), ("pick_apple", "collect some apples"), ("pick_apple", "gather some apples"), ("pick_apple", "find an apple"),
        ("catch_fish", "catch a fish"), ("catch_fish", "catch some fish"), ("catch_fish", "go fishing"), ("catch_fish", "get some fish"),
        ("gather_pinecone", "get a pinecone"), ("gather_pinecone", "get some pinecones"), ("gather_pinecone", "collect pinecones"),
        ("gather_pinecone", "collect some pinecones"), ("gather_pinecone", "gather pinecones"), ("gather_pinecone", "find some pinecones"),
        ("gather_berry", "get some berries"), ("gather_berry", "collect some berries"), ("gather_berry", "gather some berries"),
        ("gather_berry", "pick some berries"), ("gather_berry", "find some berries"), ("gather_berry", "find berries"),
        ("attack", "fight it"), ("attack", "attack it"),
        ("eat", "eat it"), ("eat", "eat something"),
        ("sleep", "get some sleep"), ("sleep", "go to sleep"),
    };

    // "Nobody decided to make torches even though I gave them sticks —
    // we need better tool calling, and a regex fallback to parse out
    // intent if possible." A real, observed case: the model called
    // speak — a genuinely successful, valid tool call, not a parse
    // failure ParseToolCall's own no_tool_call fallback would ever see
    // — but the spoken line was "I'm not sure about making torches...
    // but maybe I could use it to make one?", clearly reasoning its way
    // to the right action in words without ever calling it. Scans the
    // spoken text for the phrases above under the exact same
    // MatchesIntent guard (hedge/negation/trailing-"?") LenientParseFromText
    // itself uses, then validates through BuildAction — the same real
    // target/item/availability trust boundary every other path already
    // goes through — so "maybe... make one?" correctly recovers nothing
    // (hedged, and a trailing question) while an unhedged "I'll make a
    // torch with it" does. Null means "no clearer commitment found in
    // the words — the speak stands as spoken," not an error; see
    // NpcAgent.HandleDirectPlayerRequest for the one call site, and why
    // this is scoped to just the direct-player-request path rather than
    // every ordinary turn's own speak.
    public GameAction TryRecoverCommittedAction(string spokenMessage, AvailableTargets targets)
    {
        if (string.IsNullOrWhiteSpace(spokenMessage))
            return null;

        foreach ((string action, string phrase) in SpokenIntentPhrases)
        {
            if (!MatchesIntent(spokenMessage, phrase)) continue;

            string targetId = FindMentionedId(spokenMessage, AllKnownTargetIds(targets));
            string item = FindMentionedId(spokenMessage, ItemTypes);
            ParseResult result = BuildAction(action, targetId, EmotionExtensions.Parse(""), item, 1, "", targets);
            if (result.Ok) return result.Action;
        }
        return null;
    }

    // A negation OR hedge word sitting immediately before the match ("I
    // will NOT attack", "I don't want to sleep yet", "maybe I could
    // make one?") — not real language understanding, just enough to
    // catch the likeliest ways a bare keyword-presence scan goes wrong:
    // mentioning a word is not the same as stating the intent it names,
    // and neither is musing about it out loud without committing (a
    // real, observed case: an NPC replied to a direct request with "I'm
    // not sure about making torches... but maybe I could use it to make
    // one?" — called speak, not make_torch, despite the fire being lit
    // and the stick genuinely in hand). Only looks at a short window
    // right before the match (not the whole sentence) so a hedge
    // attached to something ELSE earlier in the text doesn't wrongly
    // veto an unrelated, genuine later match — e.g. "I don't want to
    // fight, but I will follow Maren" should still recover follow. A
    // trailing "?" shortly after the match is the same signal from the
    // other direction — "...to make one?" is a genuine, unsettled
    // question, not a decision, even with no hedge word anywhere
    // nearby. Shared by LenientParseFromText above, DecideThreatResponse's
    // own fight/flee/freeze fallback below, and
    // NpcAgent.HandleDirectPlayerRequest's "recover a real commitment
    // from a hedgy speak reply" check.
    private static readonly string[] HedgeWords =
    {
        "not", "never", "no", "n't", "don't", "doesn't", "didn't", "won't", "wouldn't",
        "shouldn't", "can't", "couldn't", "isn't", "aren't",
        "maybe", "might", "perhaps", "possibly", "unsure",
    };

    private static bool MatchesIntent(string content, string word)
    {
        Match match = Regex.Match(content, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);
        if (!match.Success) return false;

        int windowStart = Math.Max(0, match.Index - 25);
        string before = content.Substring(windowStart, match.Index - windowStart);
        if (HedgeWords.Any(h => Regex.IsMatch(before, $@"\b{Regex.Escape(h)}\b\W*$", RegexOptions.IgnoreCase)))
            return false;

        int afterStart = match.Index + match.Length;
        int afterEnd = Math.Min(content.Length, afterStart + 15);
        string after = content.Substring(afterStart, afterEnd - afterStart);
        return !after.Contains('?');
    }

    private static string FindMentionedId(string content, IEnumerable<string> candidates) =>
        candidates
            .Where(id => !string.IsNullOrEmpty(id))
            .OrderByDescending(id => id.Length)
            .FirstOrDefault(id => content.Contains(id, StringComparison.OrdinalIgnoreCase)) ?? "";

    private static IEnumerable<string> AllKnownTargetIds(AvailableTargets t) =>
        (t.TreeIds ?? Array.Empty<string>())
        .Concat(t.FishingSpotIds ?? Array.Empty<string>())
        .Concat(t.PineTreeIds ?? Array.Empty<string>())
        .Concat(t.BerryBushIds ?? Array.Empty<string>())
        .Concat(t.TravelTargetIds ?? Array.Empty<string>())
        .Concat(t.NearbyNpcNames ?? Array.Empty<string>())
        .Concat(t.AnimalIds ?? Array.Empty<string>())
        .Concat(t.StickIds ?? Array.Empty<string>())
        .Append("home")
        .Append("firepit");

    // The single trust boundary between a candidate (name, targetId,
    // ...) tuple — from either a real structured tool call or
    // LenientParseFromText's own generous text scan above — and a
    // GameAction the engine is allowed to act on. An unrecognized
    // action name or a target that isn't in the world right now is
    // rejected here, explicitly, rather than assumed valid, regardless
    // of which path it came from.
    private ParseResult BuildAction(string name, string targetId, Emotion emotion, string item, int amount, string spokenMessage, AvailableTargets targets)
    {
        // "A stick," "an apple," "a fish" — every one of these five
        // targets is generic and impersonal: nobody, in speech OR in a
        // small model's own tool-call arguments, ever names WHICH
        // tree/bush/spot/stick they mean, unlike follow/trade/steal/
        // attack, which target an actual named character or animal
        // where guessing would mean picking the WRONG specific person.
        // Same "default rather than fail on an unambiguous omission" as
        // deposit's own long-standing enum-of-one case just below,
        // generalized to an enum-of-many: default to whichever entry is
        // FIRST, which NpcAgent's own TreeIds()/FishingSpotIds()/
        // PineTreeIds()/BerryBushIds()/StickIds() now sort nearest-to-
        // this-NPC first specifically so "first" means something real.
        // This is what makes "Finn, go find a stick" actually able to
        // resolve to a real, nearby stick_id — TryRecoverCommittedAction
        // can find the PHRASE "find a stick" in what Finn said, but
        // spoken English never contains the literal id "stick_3," so
        // without this default every recovery attempt for these five
        // actions had no legal target and silently failed every time,
        // even when the phrase matched perfectly.
        if (targetId == "")
            targetId = name switch
            {
                "pick_apple" => targets.TreeIds?.FirstOrDefault() ?? "",
                "catch_fish" => targets.FishingSpotIds?.FirstOrDefault() ?? "",
                "gather_pinecone" => targets.PineTreeIds?.FirstOrDefault() ?? "",
                "gather_berry" => targets.BerryBushIds?.FirstOrDefault() ?? "",
                "pick_up_stick" => targets.StickIds?.FirstOrDefault() ?? "",
                _ => targetId,
            };

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
                // spokenMessage arrives as a parameter now — extracted
                // by whichever caller matched (the real tool-call args,
                // or LenientParseFromText's own "just use what it
                // actually wrote" fallback) — not re-extracted here.
                if (string.IsNullOrWhiteSpace(spokenMessage))
                    return ParseResult.Fail("empty_message");
                return ParseResult.Success(new GameAction(name, "", 0f, emotion, spokenMessage));
            case "follow":
                if (Array.IndexOf(targets.NearbyNpcNames, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.Follow, emotion));
            case "trade":
                if (Array.IndexOf(targets.NearbyNpcNames, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                // Can only ever offer what you actually have — the tool
                // schema already restricts the enum to carriedItems, but
                // a small model can still miss the enum, so this is the
                // real trust boundary, not just the schema.
                if (Array.IndexOf(targets.CarriedItems, item) < 0)
                    return ParseResult.Fail($"invalid_item_{item}");
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.Trade, emotion, item: item, amount: amount));
            case "steal":
                if (Array.IndexOf(targets.NearbyNpcNames, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                if (Array.IndexOf(ItemTypes, item) < 0)
                    return ParseResult.Fail($"invalid_item_{item}");
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.Steal, emotion, item: item, amount: amount));
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

    // PromptDebugLogger's own use only — the names of whatever BuildTools()/
    // BuildThreatTools() just built, for a readable "TOOLS OFFERED" line
    // without dumping the full schema on every logged turn (see that
    // class's own header for why). `dynamic` here rather than a shared
    // interface: the tools array is anonymous types built fresh in three
    // different places (BuildTools, BuildThreatTools), and adding a real
    // type just for this debug-only reflection would be more ceremony
    // than the one-line accessor it replaces.
    private static string[] ToolNames(object[] tools) =>
        tools == null ? Array.Empty<string>() : tools.Select(t => (string)((dynamic)t).function.name).ToArray();

    private static object[] BuildTools(AvailableTargets targets)
    {
        var tools = new System.Collections.Generic.List<object>
        {
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

        // pick_apple/catch_fish used to sit unconditionally in the base
        // tools list above — "hand-placed and guaranteed to exist from
        // the start," so the enum could never be empty. That guarantee
        // covered EXISTENCE, not PROXIMITY: TreeIds/FishingSpotIds are
        // vision-filtered now (see NpcAgent.SortByDistance's own header
        // — llm_tuning's baseline eval, 2026-09-08, measured pick_apple
        // recall collapsing to 8.3% specifically because it was offered
        // every turn regardless of whether an apple tree was anywhere
        // in sight), so the enum genuinely can be empty now — same
        // enum-of-nothing guard every other resource tool already
        // needed, just newly true for these two as well.
        if (targets.TreeIds.Length > 0)
        {
            tools.Add(new
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
            });
        }

        if (targets.FishingSpotIds.Length > 0)
        {
            tools.Add(new
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
            });
        }

        // Only offered when there's actually a pine tree to gather
        // from — an enum-of-nothing tool can never be called validly,
        // so omit it rather than offer a dead option. Same reasoning
        // pick_apple/catch_fish above now need too.
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
