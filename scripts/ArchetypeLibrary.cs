using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

// Loads personality_archetypes.json at the project root — checked into
// git like npcs.json, since this is game content, not a secret. Missing
// file or any parse failure falls back to the built-in set below, so
// npcs.json referencing an archetype never crashes NPC creation.
public static class ArchetypeLibrary
{
    private const string LibraryPath = "personality_archetypes.json";

    private static readonly List<PersonalityArchetype> Defaults = new()
    {
        new PersonalityArchetype { Id = "steady_thinker", Description = "Grounded, a little guarded, thinks before acting.", Openness = 0.6f, Conscientiousness = 0.7f, Extraversion = 0.3f, Agreeableness = 0.5f, Neuroticism = 0.4f },
        new PersonalityArchetype { Id = "easygoing_wanderer", Description = "Curious, sociable, doesn't stress much.", Openness = 0.7f, Conscientiousness = 0.4f, Extraversion = 0.6f, Agreeableness = 0.6f, Neuroticism = 0.3f },
        new PersonalityArchetype { Id = "cautious_homebody", Description = "Prefers routine and the familiar; slow to warm up.", Openness = 0.3f, Conscientiousness = 0.8f, Extraversion = 0.2f, Agreeableness = 0.5f, Neuroticism = 0.6f },
        new PersonalityArchetype { Id = "bold_adventurer", Description = "Chases novelty, impatient with caution.", Openness = 0.9f, Conscientiousness = 0.4f, Extraversion = 0.7f, Agreeableness = 0.4f, Neuroticism = 0.3f },
        new PersonalityArchetype { Id = "gruff_loner", Description = "Keeps to themselves, blunt, hard to read.", Openness = 0.4f, Conscientiousness = 0.6f, Extraversion = 0.1f, Agreeableness = 0.2f, Neuroticism = 0.5f },
        new PersonalityArchetype { Id = "warm_socializer", Description = "Thrives around others, quick to trust.", Openness = 0.6f, Conscientiousness = 0.5f, Extraversion = 0.9f, Agreeableness = 0.8f, Neuroticism = 0.3f },
        new PersonalityArchetype { Id = "anxious_worrier", Description = "Careful to a fault, easily rattled.", Openness = 0.4f, Conscientiousness = 0.7f, Extraversion = 0.3f, Agreeableness = 0.6f, Neuroticism = 0.85f },
        new PersonalityArchetype { Id = "calm_pragmatist", Description = "Even-keeled, focused on what works.", Openness = 0.5f, Conscientiousness = 0.8f, Extraversion = 0.4f, Agreeableness = 0.5f, Neuroticism = 0.15f },
        new PersonalityArchetype { Id = "wanderer_at_heart", Description = "Can't sit still; always eyeing the horizon, unbothered by not knowing what's out there.", Openness = 0.9f, Conscientiousness = 0.25f, Extraversion = 0.55f, Agreeableness = 0.5f, Neuroticism = 0.2f },
    };

    private static Dictionary<string, PersonalityArchetype> _byId;

    private static Dictionary<string, PersonalityArchetype> Loaded
    {
        get
        {
            if (_byId != null)
                return _byId;

            List<PersonalityArchetype> list = Defaults;
            try
            {
                if (File.Exists(LibraryPath))
                {
                    string json = File.ReadAllText(LibraryPath);
                    var loaded = JsonSerializer.Deserialize<List<PersonalityArchetype>>(json);
                    if (loaded is { Count: > 0 })
                        list = loaded;
                }
            }
            catch (Exception)
            {
                // malformed library — run on built-in defaults rather than crash
            }

            _byId = new Dictionary<string, PersonalityArchetype>();
            foreach (PersonalityArchetype a in list)
                _byId[a.Id] = a;
            return _byId;
        }
    }

    // An unknown or missing archetype id (a typo in npcs.json, say)
    // falls back to a neutral shape rather than crashing NPC creation.
    public static PersonalityArchetype Get(string id)
    {
        if (!string.IsNullOrEmpty(id) && Loaded.TryGetValue(id, out PersonalityArchetype found))
            return found;
        return Loaded.TryGetValue("calm_pragmatist", out PersonalityArchetype fallback) ? fallback : Defaults[^1];
    }
}
