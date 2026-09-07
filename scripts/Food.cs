using System.Collections.Generic;

// What "eat" can actually consume, and how much Health each restores —
// not everything carried is food (a pinecone or a stick isn't). Health
// restored is roughly "how filling" — fish more than a single berry.
public static class Food
{
    private static readonly Dictionary<string, float> HealthRestored = new()
    {
        { "fish", 20f }, { "apple", 15f },
        { "blueberry", 8f }, { "blackberry", 8f }, { "raspberry", 8f },
    };

    public static bool IsFood(string item) => HealthRestored.ContainsKey(item);

    // The first food item found on hand, in the fixed priority order
    // above (more filling first) — eat doesn't ask which food, same as
    // gathering doesn't ask which apple.
    public static string BestFoodIn(Inventory inventory)
    {
        foreach (string item in HealthRestored.Keys)
            if (inventory.Has(item))
                return item;
        return null;
    }

    public static float HealthFor(string item) => HealthRestored.TryGetValue(item, out float v) ? v : 0f;
}
