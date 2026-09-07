// What a human's attack actually deals before Strength is added on top
// — checked against what's actually in Inventory at the moment of the
// attack, same "the real thing you're carrying, not a hint" trust
// boundary trade/steal already use for items. One place to raise the
// day a second real weapon exists, same as ActionRanges/ItemTypes
// already do per-action/per-item.
public static class Weapons
{
    public const int UnarmedDamage = 2;
    public const int StickDamage = 5;

    public static int BestDamage(Inventory inventory) =>
        inventory.Has("stick") ? StickDamage : UnarmedDamage;
}
