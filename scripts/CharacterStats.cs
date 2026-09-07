// Classic D&D six-stat block, plus one: Bravery. Rolled fresh (3d6
// each, 3-18 range) for every NPCActor at construction — including
// PlayerCharacter, since it IS an NPCActor — so the player gets the
// same kind of randomized capability profile an NPC does, no
// special-casing. These are deliberately separate from Personality's
// OCEAN traits: Personality models how someone WANTS to act
// (psychology, feeds the LLM's reasoning); CharacterStats models how
// capable they are of pulling it off (mechanics, feeds dice checks). A
// high-Openness, low-Strength character can still want to pick a
// fight; the stats are what decide whether that goes well.
//
// Bravery is the one addition to the classic six, and it's deliberately
// mechanical-only, not LLM-facing (Personality.DescribeForPrompt()
// never mentions it) — it governs the dice-driven parts of the
// fight/flee/freeze and alert systems (NpcAgent's own
// MaybeCowardiceOverride, RandomThreatFallback, HandleAlert): whether
// an NPC's default disposition toward a sighted predator or a friend's
// fight is to help/engage or hang back. It's still rolled for
// PlayerCharacter (nothing here special-cases who gets one, same as
// every other stat), it just never ends up READ for the player, since
// the player has no danger-response turn loop to feed it into — their
// own fight/flee/freeze is whatever they actually choose to click or
// type, not a roll. "Human players don't roll for it, that's their own
// choice" in practice just falls out of nothing player-side ever
// consulting it, not a special exemption anywhere in this class.
public class CharacterStats
{
    public int Strength;
    public int Dexterity;
    public int Constitution;
    public int Intelligence;
    public int Wisdom;
    public int Charisma;
    public int Bravery;

    public CharacterStats()
    {
        Strength = Dice.ThreeD6();
        Dexterity = Dice.ThreeD6();
        Constitution = Dice.ThreeD6();
        Intelligence = Dice.ThreeD6();
        Wisdom = Dice.ThreeD6();
        Charisma = Dice.ThreeD6();
        Bravery = Dice.ThreeD6();
    }

    // Standard D&D modifier curve: 10-11 is average (+0), each 2 points
    // above or below shifts the modifier by 1. Floor, not truncation —
    // matters for odd scores below 10 (e.g. 9 -> -1, not 0).
    public static int Modifier(int score) => (int)System.Math.Floor((score - 10) / 2.0);

    public int StrengthMod => Modifier(Strength);
    public int DexterityMod => Modifier(Dexterity);
    public int ConstitutionMod => Modifier(Constitution);
    public int IntelligenceMod => Modifier(Intelligence);
    public int WisdomMod => Modifier(Wisdom);
    public int CharismaMod => Modifier(Charisma);
    public int BraveryMod => Modifier(Bravery);

    public string Describe() =>
        $"STR {Strength} DEX {Dexterity} CON {Constitution} INT {Intelligence} WIS {Wisdom} CHA {Charisma} BRV {Bravery}";
}
