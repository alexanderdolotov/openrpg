// The one place ANY fight actually resolves — human vs animal, animal
// vs human, animal vs animal, and (nothing stops it) human vs human
// later — so there's exactly one combat formula in the whole game,
// not a separate one per attacker type. An opposed roll: the
// attacker's own combined offense (Strength + Dexterity) against a DC
// built from the defender's Dexterity (how hard they actually are to
// land a hit on) — a hit deals real damage, scaled by the attacker's
// own Strength and whatever weapon they're carrying (see Weapons.cs).
// Same shape as every other opposed check in the game (steal, the
// persuasion hint) — SkillCheck.Roll(offense, DifficultyClass.
// OpposedBase + defense) — just applied to a fight instead.
public static class Combat
{
    public readonly struct Result
    {
        public readonly bool Hit;
        public readonly int Damage;
        public readonly SkillCheck Check;

        public Result(bool hit, int damage, SkillCheck check)
        {
            Hit = hit;
            Damage = damage;
            Check = check;
        }
    }

    // baseDamage: the weapon's own damage (Weapons.BestDamage() for a
    // human, or an animal's own bite/claw number) — Strength only adds
    // ON TOP of that, it's never the whole story on its own.
    public static Result Resolve(int attackerStrengthMod, int attackerDexterityMod, int defenderDexterityMod, int baseDamage)
    {
        var check = SkillCheck.Roll(attackerStrengthMod + attackerDexterityMod, DifficultyClass.OpposedBase + defenderDexterityMod);
        int damage = check.Success ? System.Math.Max(1, baseDamage + attackerStrengthMod) : 0;
        return new Result(check.Success, damage, check);
    }
}
