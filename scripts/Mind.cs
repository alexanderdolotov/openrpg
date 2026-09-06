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
// steal, persuade, sleep, wait — is offered identically to every NPC's
// tool schema (some gated on context: travel/follow/trade/steal/persuade
// only appear when there's actually a valid target, never as a dead
// enum-of-nothing option). Nothing in this file, or anywhere else,
// decides which NPC "is" the fisher, or the thief, or the leader.
// Whatever role an NPC settles into is purely a consequence of the model
// reading its Personality.DescribeForPrompt() framing and choosing
// accordingly.
public class Mind
{
    // Instruction only — "how to respond." "Who you are" comes from
    // Personality.DescribeForPrompt(), prepended per-call in Decide()
    // so the same instruction serves any NPC's personality.
    private const string ThinkInstruction =
        "Given the situation below, write ONE short, plain sentence (under 15 words) of what you're actually thinking right now. Talk like a real person thinking to themselves, not a novelist — no metaphors, no describing the scenery, no flowery language. If your last action failed, especially more than once, say so plainly and react to it. Just the plain thought, nothing else.";
    private const string ActInstruction =
        "Call exactly one of the provided tools that matches the plan below. If your last action failed — especially if it failed more than once in a row for the same reason — do not just repeat it; pick something that actually addresses why it failed (e.g. deposit before trying to pick or catch again, or choose a different target if one is depleted). travel is a valid choice on its own, out of curiosity, even toward somewhere you've never been and don't know the way to — you don't need a resource-gathering reason to go look at something. speak lets you say something out loud in your own words — anyone within hearing range right now may hear it and decide how to react on their own later; it does not control or compel anyone, and if nobody's around, nobody hears it, which is a perfectly normal outcome of speaking. follow lets you walk alongside someone nearby by name — a genuine choice you make (or don't) based on your own read of them and what's been said, not something anyone can force; re-decide it fresh every turn just like anything else, so if your mood shifts or you're no longer convinced, choosing something other than follow is exactly how you stop. trade gives someone nearby an item you're actually carrying — a real choice about generosity or self-interest, entirely up to you. steal takes an item from someone nearby without asking and without them agreeing to it — you don't know for certain what they're carrying, only a guess, it takes real nerve and a little luck (you can simply fail even if they do have it), and it's not hidden from them forever, since anyone keeps their own count of what they're carrying and can notice later that something's missing. persuade is a real, focused attempt to talk someone nearby into something — how well it lands depends on you, not just what you say, and it never forces their next decision either way; a failed attempt is worth noticing and not just repeating verbatim. sleep rests where you are and fully restores your fatigue, but takes a while — worth doing once you're actually tired, not as a routine choice, and pay attention to your own fatigue level below: if you're exhausted, that's a real, physical reason to sleep before doing anything else, or to turn down something demanding (a long trip, more gathering) rather than push through it — nobody is forcing that consideration on you, it's just true of your own body right now. Set emotion to how you're genuinely feeling, reacting to what just happened as much as your personality — frustration or disappointment after a repeated failure, satisfaction after a success, not a fixed mood. Only ever target an id that is explicitly listed in the situation — never invent one that isn't there. Choose whichever tool actually fits who you are and what you want right now — nothing assigns you a role.";
    private const string SummarizeSystemPrompt =
        "You are compressing an NPC's memory log into a short diary paragraph (3-5 sentences) they'll carry forward. Preserve what matters for future decisions — where they've been, what they've done, anything notable, and anything said aloud (by them or heard from someone else), including who said or asked for what by name. A repeated identical failure is NOT routine detail — it's the opposite: state plainly what failed, why, and how many times, so it isn't attempted again pointlessly. Drop only genuinely routine, non-repeated detail (a single successful wait, a normal walk). Write in first person, past tense.";

    private static readonly string[] ValidActions = { "pick_apple", "catch_fish", "deposit", "travel", "speak", "follow", "trade", "steal", "persuade", "sleep", "wait" };

    // Every item type that currently exists in the world — trade/steal
    // both need a fixed, enumerable answer to "which item" for the tool
    // schema. Grows the day a new resource type does, same as
    // ActionRanges already does per-action.
    private static readonly string[] ItemTypes = { "apple", "fish" };

    private readonly ILlmProvider _provider;

    public Mind(ILlmProvider provider)
    {
        _provider = provider;
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

    public async Task<MindResult> Decide(string perceptionText, string[] treeIds, string[] fishingSpotIds, string[] travelTargetIds, string[] nearbyNpcNames, string[] carriedItems, Personality personality)
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
        object[] tools = BuildTools(treeIds, fishingSpotIds, travelTargetIds, nearbyNpcNames, carriedItems);

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

            parsed = ParseToolCall(actResult.Message, treeIds, fishingSpotIds, travelTargetIds, nearbyNpcNames, carriedItems);
            if (parsed.Ok)
                return MindResult.Success(thought, parsed.Action);
        }

        return MindResult.Fail(parsed.Error, thought);
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
    private ParseResult ParseToolCall(ChatMessage message, string[] treeIds, string[] fishingSpotIds, string[] travelTargetIds, string[] nearbyNpcNames, string[] carriedItems)
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
                if (Array.IndexOf(treeIds, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.PickApple, emotion));
            case "catch_fish":
                if (Array.IndexOf(fishingSpotIds, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.CatchFish, emotion));
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
                if (Array.IndexOf(travelTargetIds, targetId) < 0)
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
                if (Array.IndexOf(nearbyNpcNames, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.Follow, emotion));
            case "trade":
            {
                if (Array.IndexOf(nearbyNpcNames, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                string item = ExtractField(fn.Arguments, "item");
                // Can only ever offer what you actually have — the tool
                // schema already restricts the enum to carriedItems, but
                // a small model can still miss the enum, so this is the
                // real trust boundary, not just the schema.
                if (Array.IndexOf(carriedItems, item) < 0)
                    return ParseResult.Fail($"invalid_item_{item}");
                int amount = ParseAmount(fn.Arguments);
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.Trade, emotion, item: item, amount: amount));
            }
            case "steal":
            {
                if (Array.IndexOf(nearbyNpcNames, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                string item = ExtractField(fn.Arguments, "item");
                if (Array.IndexOf(ItemTypes, item) < 0)
                    return ParseResult.Fail($"invalid_item_{item}");
                int amount = ParseAmount(fn.Arguments);
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.Steal, emotion, item: item, amount: amount));
            }
            case "persuade":
            {
                if (Array.IndexOf(nearbyNpcNames, targetId) < 0)
                    return ParseResult.Fail($"invalid_target_{targetId}");
                string pitch = ExtractField(fn.Arguments, "message");
                if (string.IsNullOrWhiteSpace(pitch))
                    return ParseResult.Fail("empty_message");
                return ParseResult.Success(new GameAction(name, targetId, ActionRanges.Persuade, emotion, message: pitch));
            }
            case "sleep":
                return ParseResult.Success(new GameAction(name, "", 0f, emotion));
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

    private static object[] BuildTools(string[] treeIds, string[] fishingSpotIds, string[] travelTargetIds, string[] nearbyNpcNames, string[] carriedItems)
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
                            target_id = new { type = "string", @enum = treeIds, description = "which tree to pick from" },
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
                            target_id = new { type = "string", @enum = fishingSpotIds, description = "which fishing spot to try" },
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
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "speak",
                    description = "Say something out loud, in your own words. Anyone within hearing range right now may hear it and decide how to react on their own, later — this does not control or compel anyone, and if nobody's around, nobody hears it.",
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

        // Only offered when there's actually a flagpole to name — an
        // enum-of-nothing tool can never be called validly, so omit it
        // rather than offer a dead option.
        if (travelTargetIds.Length > 0)
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
                            target_id = new { type = "string", @enum = travelTargetIds, description = "which distant landmark to head toward" },
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "target_id", "emotion" },
                    },
                },
            });
        }

        // Same reasoning as travel — offered only when there's actually
        // someone nameable nearby to follow, so the enum is never empty.
        if (nearbyNpcNames.Length > 0)
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
                            target_id = new { type = "string", @enum = nearbyNpcNames, description = "the name of who to follow" },
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
        if (nearbyNpcNames.Length > 0 && carriedItems.Length > 0)
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
                            target_id = new { type = "string", @enum = nearbyNpcNames, description = "who to give it to" },
                            item = new { type = "string", @enum = carriedItems, description = "which item to give — only what you're actually carrying" },
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
        if (nearbyNpcNames.Length > 0)
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
                            target_id = new { type = "string", @enum = nearbyNpcNames, description = "who to take it from" },
                            item = new { type = "string", @enum = ItemTypes, description = "which item to try to take" },
                            amount = new { type = "integer", description = "how many to try to take (defaults to 1)" },
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "target_id", "item", "emotion" },
                    },
                },
            });
        }

        // persuade: same nearby-target gating as follow/trade/steal.
        if (nearbyNpcNames.Length > 0)
        {
            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "persuade",
                    description = "Make a real, focused attempt to talk someone nearby into something specific. How well it lands depends on you as much as your words — it can fail — and it never forces their next decision either way.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            target_id = new { type = "string", @enum = nearbyNpcNames, description = "who you're trying to persuade" },
                            message = new { type = "string", description = "what you're trying to convince them of, in your own words" },
                            emotion = new { type = "string", @enum = EmotionExtensions.AllValues, description = "how you're feeling right now" },
                        },
                        required = new[] { "target_id", "message", "emotion" },
                    },
                },
            });
        }

        return tools.ToArray();
    }
}
