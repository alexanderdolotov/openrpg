using System.Linq;

// A small, deliberately fixed vocabulary rather than free text — a
// closed set is what makes "represent this as an emoji/face" trivial
// later (one asset per value), and keeps a small model from inventing
// an endless variety of moods it can't stay consistent about turn to
// turn.
public enum Emotion
{
    Neutral,
    Happy,
    Sad,
    Excited,
    Fearful,
    Angry,
    Curious,
    Content,
}

public static class EmotionExtensions
{
    // Lowercased names, built once — this is what gets offered to the
    // model as the tool schema's enum and what gets parsed back.
    public static readonly string[] AllValues =
        System.Enum.GetNames(typeof(Emotion)).Select(n => n.ToLowerInvariant()).ToArray();

    public static string ToWireString(this Emotion emotion) => emotion.ToString().ToLowerInvariant();

    public static Emotion Parse(string value, Emotion fallback = Emotion.Neutral)
    {
        if (!string.IsNullOrEmpty(value) && System.Enum.TryParse(value, true, out Emotion parsed))
            return parsed;
        return fallback;
    }
}
