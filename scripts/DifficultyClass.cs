// Target numbers for SkillCheck.Roll() — one source, same reasoning as
// ActionRanges: previously-magic numbers collected in one place so
// tuning "how hard is stealing" doesn't mean hunting through the file
// that actually resolves the action.
public static class DifficultyClass
{
    // Gathering (pick_apple, catch_fish) only takes a little dexterity
    // — this is deliberately low so it almost always succeeds, but a
    // very low-Dexterity character can still fumble occasionally.
    public const int Gather = 5;

    // Stealing is a real contest: the flat number here is added to the
    // TARGET's own relevant modifier at the call site, so a more
    // dexterous target is genuinely harder to steal from — not just a
    // fixed number everyone faces equally.
    public const int OpposedBase = 10;
}
