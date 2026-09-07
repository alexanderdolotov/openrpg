using System.Collections.Generic;

// What "eat" can actually consume, and how much it restores — not
// everything carried is food (a pinecone or a stick isn't). Two
// separate numbers per item, not one: HungerRestored (the primary
// reason to eat — Hunger decays passively for everyone now, same as
// Fatigue, see Vitals) and HealthRestored (a real meal also helps you
// feel better, a smaller amount on top, the ORIGINAL reason eat existed
// before Hunger did — see NPCActor.CanEat()'s own two-threshold
// comment). Both are roughly "how filling" — fish more than a single
// berry.
public static class Food
{
    // cooked_meat first — deliberately the most filling thing in the
    // game (better than fish), the actual payoff for "catch a rabbit,
    // cook it at the fire pit" being real effort (hunt it down, carry
    // the meat home, need the fire lit) rather than just another
    // berry. Raw "rabbit_meat" is NOT in either dictionary — IsFood()
    // says no, same as a pinecone — cooking it (FirePit's own
    // "cook_meat") is what turns it into something actually edible;
    // eating the cooked result doesn't need the fire pit at all,
    // unlike making it.
    private static readonly Dictionary<string, float> HungerRestored = new()
    {
        { "cooked_meat", 60f }, { "fish", 45f }, { "apple", 30f },
        { "blueberry", 15f }, { "blackberry", 15f }, { "raspberry", 15f },
    };

    private static readonly Dictionary<string, float> HealthRestored = new()
    {
        { "cooked_meat", 35f }, { "fish", 20f }, { "apple", 15f },
        { "blueberry", 8f }, { "blackberry", 8f }, { "raspberry", 8f },
    };

    public static bool IsFood(string item) => HungerRestored.ContainsKey(item);

    // The first food item found on hand, in the fixed priority order
    // above (more filling first) — eat doesn't ask which food, same as
    // gathering doesn't ask which apple.
    public static string BestFoodIn(Inventory inventory)
    {
        foreach (string item in HungerRestored.Keys)
            if (inventory.Has(item))
                return item;
        return null;
    }

    public static float HungerFor(string item) => HungerRestored.TryGetValue(item, out float v) ? v : 0f;
    public static float HealthFor(string item) => HealthRestored.TryGetValue(item, out float v) ? v : 0f;
}
