using System.Text.Json.Serialization;

// The data shape for one row of npcs.json. Deliberately Godot-free
// (like Personality) — plain floats and strings, with conversion to
// engine types (Vector2, Color) left to whoever actually spawns the
// NPC, same separation Personality already keeps.
//
// Personality traits are NOT spelled out here — "archetype" references
// a named, reusable shape from personality_archetypes.json (via
// ArchetypeLibrary). What's specific to this character stays here
// (name, backstory, position, color); what's a reusable personality
// shape lives in the archetype pool instead.
public class NpcDefinition
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("backstory")] public string Backstory { get; set; } = "";
    [JsonPropertyName("archetype")] public string Archetype { get; set; } = "";

    [JsonPropertyName("start_x")] public float StartX { get; set; } = 200f;
    [JsonPropertyName("start_y")] public float StartY { get; set; } = 200f;
    [JsonPropertyName("color")] public string Color { get; set; } = "#808080"; // HTML hex, parsed by Godot's Color(string)

    public Personality ToPersonality()
    {
        PersonalityArchetype shape = ArchetypeLibrary.Get(Archetype);
        return new Personality
        {
            Name = Name,
            Backstory = Backstory,
            Openness = shape.Openness,
            Conscientiousness = shape.Conscientiousness,
            Extraversion = shape.Extraversion,
            Agreeableness = shape.Agreeableness,
            Neuroticism = shape.Neuroticism,
        };
    }
}
