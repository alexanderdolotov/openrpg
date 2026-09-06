using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

// Loads the NPC roster from npcs.json at the project root — checked
// into git, unlike mind.local.json, since this is game content, not a
// secret. Missing file or any parse failure falls back to the built-in
// three below, so the game never ends up with zero NPCs — same "never
// crash on bad external input" posture as MindConfig.
public static class NpcRoster
{
    private const string RosterPath = "npcs.json";

    private static readonly List<NpcDefinition> Defaults = new()
    {
        new NpcDefinition
        {
            Id = "npc_0",
            Name = "Maren",
            Backstory = "A blacksmith's apprentice who comes to this garden looking for a quiet place to think.",
            Archetype = "steady_thinker",
            StartX = 210,
            StartY = 200,
            Color = "#2650D9",
        },
        new NpcDefinition
        {
            Id = "npc_1",
            Name = "Finn",
            Backstory = "Grew up on a fishing boat and feels most himself with his feet in the water, though he's never turned down a good apple either.",
            Archetype = "easygoing_wanderer",
            StartX = 250,
            StartY = 270,
            Color = "#D9731A",
        },
        new NpcDefinition
        {
            Id = "npc_2",
            Name = "Wren",
            Backstory = "Has never stayed anywhere long. Every hazy shape on the horizon is an open question, and Wren has never been good at leaving those alone.",
            Archetype = "wanderer_at_heart",
            StartX = 170,
            StartY = 260,
            Color = "#4CBF59",
        },
    };

    public static List<NpcDefinition> Load()
    {
        try
        {
            if (File.Exists(RosterPath))
            {
                string json = File.ReadAllText(RosterPath);
                var loaded = JsonSerializer.Deserialize<List<NpcDefinition>>(json);
                if (loaded is { Count: > 0 })
                    return loaded;
            }
        }
        catch (Exception)
        {
            // malformed roster file — run on defaults rather than crash
        }

        return Defaults;
    }
}
