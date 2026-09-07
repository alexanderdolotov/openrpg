using System;

// One shared RNG source for every dice-shaped decision in the game —
// stat rolls at spawn, skill checks during play. Centralized so nothing
// else has to new up its own Random (and risk the classic bug of two
// Randoms seeded from the clock in the same tick producing identical
// sequences).
public static class Dice
{
    private static readonly Random Rng = new();

    // Inclusive on both ends, like a real die: Roll(20) returns 1-20.
    public static int Roll(int sides) => Rng.Next(1, sides + 1);

    public static int D20() => Roll(20);

    // Classic D&D ability score generation: three six-sided dice, summed
    // — a bell curve centered on 10-11, range 3-18.
    public static int ThreeD6() => Roll(6) + Roll(6) + Roll(6);

    // A random float in [min, max) — for continuous-value rolls
    // (placement jitter, and the like) that don't fit the discrete
    // Roll(sides) shape above. Same shared Rng, so it's still one RNG
    // source for the whole game, not a second Random risking the
    // clock-seed collision this class exists to avoid.
    public static float FloatRange(float min, float max) => min + (float)Rng.NextDouble() * (max - min);
}
