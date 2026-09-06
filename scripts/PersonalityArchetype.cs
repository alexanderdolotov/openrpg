using System.Text.Json.Serialization;

// One reusable personality "shape" — a named trait set an NPC can be
// assigned in npcs.json instead of spelling out five numbers per
// character. See personality_archetypes.json for the actual pool.
public class PersonalityArchetype
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = ""; // human reference only, never sent to the LLM

    [JsonPropertyName("openness")] public float Openness { get; set; } = 0.5f;
    [JsonPropertyName("conscientiousness")] public float Conscientiousness { get; set; } = 0.5f;
    [JsonPropertyName("extraversion")] public float Extraversion { get; set; } = 0.5f;
    [JsonPropertyName("agreeableness")] public float Agreeableness { get; set; } = 0.5f;
    [JsonPropertyName("neuroticism")] public float Neuroticism { get; set; } = 0.5f;
}
