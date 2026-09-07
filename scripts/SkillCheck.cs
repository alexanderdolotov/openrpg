// One shared d20-plus-modifier-vs-DC roll, used everywhere a character's
// stats should have a say in whether an action actually works — gathering,
// stealing, and anything added after. Centralizing this is
// what makes "log the roll" consistent everywhere instead of every call
// site hand-rolling its own dictionary of numbers.
public readonly struct SkillCheck
{
    public readonly int D20;
    public readonly int Modifier;
    public readonly int Total;
    public readonly int Dc;
    public readonly bool Success;

    public SkillCheck(int d20, int modifier, int dc)
    {
        D20 = d20;
        Modifier = modifier;
        Total = d20 + modifier;
        Dc = dc;
        Success = Total >= dc;
    }

    public static SkillCheck Roll(int modifier, int dc) => new(Dice.D20(), modifier, dc);

    // A short, log-ready breakdown — "d20(14) + 1 = 15 vs DC 5" — every
    // caller that logs a check result uses exactly this, not a
    // hand-formatted string of its own.
    public string Describe() => $"d20({D20}) {(Modifier >= 0 ? "+" : "-")} {System.Math.Abs(Modifier)} = {Total} vs DC {Dc}";

    // Packed into a GameAction/InteractResult's data dictionary so the
    // engine's result (Finish()/TryInteract()) can carry the full roll
    // upstream to whatever logs it, not just the pass/fail bit.
    public Godot.Collections.Dictionary ToData(string checkName)
    {
        return new Godot.Collections.Dictionary
        {
            { "check", checkName },
            { "d20", D20 },
            { "modifier", Modifier },
            { "total", Total },
            { "dc", Dc },
        };
    }

    // The other direction from ToData() — by the time NpcAgent or
    // PlayerCharacter see a result, it's crossed the ActionCompleted
    // signal as a Godot Dictionary, not this struct, so Describe() alone
    // isn't reachable from there; this is what both of them call instead
    // of each hand-formatting the same bracketed summary. Returns "" for
    // a result that didn't come from a check at all (most don't).
    public static string SummarizeData(Godot.Collections.Dictionary data)
    {
        if (!data.ContainsKey("check"))
            return "";
        string checkName = data["check"].AsString();
        int d20 = data["d20"].AsInt32();
        int modifier = data["modifier"].AsInt32();
        int dc = data["dc"].AsInt32();
        // Total is also in `data`, but reconstructing it here (rather
        // than trusting a sixth round-tripped number) and rebuilding the
        // struct means this goes through Describe() — one format string,
        // not two copies of it drifting apart later.
        var check = new SkillCheck(d20, modifier, dc);
        return $" [{checkName} check: {check.Describe()}]";
    }
}
