using Godot;

// Anything that can take part in a fight — dealt damage, asked its own
// offense/defense numbers, and asked whether it's already down. Lets
// Combat.Resolve() (and whatever calls it) treat a human and an animal
// identically: NPCActor implements this directly (covering both NPCs
// and PlayerCharacter, which extends it), and Animal implements it too
// — same interface, two very different underlying stat models (see
// Animal's own header for why animals don't share CharacterStats).
public interface ICombatant
{
    Vector2 GlobalPosition { get; }
    int StrengthMod { get; }
    int DexterityMod { get; }

    // Already incapacitated/dead — a target that's already down can't
    // be attacked again (no piling on).
    bool IsDown { get; }

    // attacker: who dealt this, if known/relevant — NPCActor ignores
    // it (a human's own retaliation runs through its LLM/FFF decision
    // loop instead, not an instant reflex), but Animal's non-LLM AI
    // needs it immediately to know who to fight back against.
    void ReceiveDamage(int amount, ICombatant attacker = null);
}
