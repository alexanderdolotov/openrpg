// What the mind layer hands down. Deliberately thin — an id and a
// target reference, nothing about HOW to get there. That's the whole
// point of the split: the mind decides WHAT, the engine figures out HOW.
// Plain C# class, not a Godot type — it never crosses a signal boundary,
// only ever passed directly between C# scripts.
public class GameAction
{
    public readonly string Id;
    public readonly string TargetId;
    public readonly float Range;

    // Null for scripted-fallback actions — there's no real feeling
    // behind a mechanical fallback, so it leaves the NPC's last actual
    // emotion alone rather than inventing one.
    public readonly Emotion? Emotion;

    // Only set for "speak" — what the NPC says out loud. Empty for
    // everything else.
    public readonly string Message;

    // Only set for "trade"/"steal" — which item, and how many. Empty
    // item / amount 1 for everything else.
    public readonly string Item;
    public readonly int Amount;

    public GameAction(string id, string targetId = "", float range = 32f, Emotion? emotion = null, string message = "", string item = "", int amount = 1)
    {
        Id = id;
        TargetId = targetId;
        Range = range;
        Emotion = emotion;
        Message = message;
        Item = item;
        Amount = amount;
    }
}
