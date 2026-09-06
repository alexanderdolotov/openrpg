using System;
using System.Collections.Generic;

// Numeric Big-Five-style traits (0-1) plus a short backstory. Traits do
// two jobs: they turn into natural-language framing for the system
// prompt (a small model reasons better from "curious and open-minded"
// than from "openness: 0.8"), and they derive a sampling temperature —
// steadier, conscientious NPCs sample cold and stay predictable turn to
// turn; open or neurotic ones sample hot and read as more erratic.
public class Personality
{
    public string Name = "NPC";
    public string Backstory = "";

    public float Openness = 0.5f;
    public float Conscientiousness = 0.5f;
    public float Extraversion = 0.5f;
    public float Agreeableness = 0.5f;
    public float Neuroticism = 0.5f;

    public float Temperature => Math.Clamp(0.35f + Openness * 0.3f + Neuroticism * 0.25f, 0.35f, 0.95f);

    public string DescribeForPrompt()
    {
        string Word(float v, string low, string mid, string high) =>
            v < 0.35f ? low : v > 0.65f ? high : mid;

        var traits = new List<string>
        {
            Word(Openness, "set in your ways", "reasonably open-minded", "very curious and open to new things"),
            Word(Conscientiousness, "impulsive and easily distracted", "fairly steady", "disciplined and careful"),
            Word(Extraversion, "quiet and reserved", "moderately sociable", "outgoing and talkative"),
            Word(Agreeableness, "blunt and guarded with strangers", "generally cooperative", "warm and trusting"),
            Word(Neuroticism, "even-tempered", "occasionally anxious", "easily rattled"),
        };

        string backstoryLine = string.IsNullOrEmpty(Backstory) ? "" : $" {Backstory}";
        return $"You are {Name}.{backstoryLine} Your personality: {string.Join(", ", traits)}.";
    }
}
