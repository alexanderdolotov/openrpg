using Godot;

// The minimal surface any character in the world exposes to every
// OTHER character's perception — whether LLM-driven (NpcAgent) or
// input-driven (PlayerCharacter). This is what lets "who's nearby,"
// SpeechLog, and follow's target list work generically instead of
// being hardcoded to NpcAgent specifically — exactly the abstraction
// the README's architecture note called for before a player character
// could exist without being forced through NpcAgent's LLM-specific
// shape.
public interface IWorldCharacter
{
    string Id { get; }
    string DisplayName { get; }
    Vector2 GlobalPosition { get; }
    Emotion CurrentEmotion { get; }
}
