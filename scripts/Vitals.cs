using Godot;

// Dynamic condition, as opposed to CharacterStats' fixed capability
// scores — Health and Fatigue actually change turn to turn, decaying
// passively over time (see NPCActor._PhysicsProcess) and draining
// faster from real exertion (gathering — see AppleTree/FishingSpot).
// Both 0-100. Shared by every NPCActor, including PlayerCharacter, same
// as Inventory and Stats — nothing here treats an NPC and the player
// differently.
public class Vitals
{
    public float Health = 100f;
    public float Fatigue = 100f; // 100 = fully rested, 0 = running on empty

    public const float LowFatigueThreshold = 25f; // below this, perception explicitly says so — the LLM decides what to do about it, nothing forces sleep
    // Above this, sleep isn't offered as an option at all (NPCActor.
    // CanSleep()) — not tired enough for it to make sense regardless of
    // location.
    public const float SleepUnnecessaryThreshold = 70f;
    public const float PassiveDecayPerSecond = 100f / 900f; // ~15 real minutes from full to empty doing nothing but standing around

    public bool NeedsSleep => Fatigue < LowFatigueThreshold;

    // True the instant a real hit (Damage()) brings Health to 0 — what
    // NPCActor's own State.Incapacitated (or, under
    // GameSettings.PermadeathEnabled, real death) actually keys off.
    public bool IsIncapacitated => Health <= 0f;

    public void DecayOverTime(float seconds)
    {
        Fatigue = Mathf.Max(0f, Fatigue - PassiveDecayPerSecond * seconds);
    }

    // Real labor costs more than just existing — called once per
    // resolved gather attempt (success or fumble; the effort was spent
    // either way).
    public void Exert(float amount)
    {
        Fatigue = Mathf.Max(0f, Fatigue - amount);
    }

    // Sleep restores fatigue fully and nudges health back up a little.
    public void Sleep()
    {
        Fatigue = 100f;
        Health = Mathf.Min(100f, Health + 10f);
    }

    // A real hit — from Combat.Resolve() — actually costs Health, down
    // to (not below) 0.
    public void Damage(float amount)
    {
        Health = Mathf.Max(0f, Health - amount);
    }

    // Waking up from being knocked out (State.Incapacitated, only when
    // GameSettings.PermadeathEnabled is off) — a partial recovery, not
    // a full heal; getting knocked out again right away is meant to
    // stay a real risk, not reset to full every time.
    public void RecoverFromKnockout()
    {
        Health = 40f;
    }

    public string Describe()
    {
        string state = Fatigue <= 0f ? "completely exhausted"
            : NeedsSleep ? "exhausted — needs sleep soon"
            : Fatigue < 60f ? "getting tired"
            : "well-rested";
        return $"health {Health:0}/100, fatigue {Fatigue:0}/100 ({state})";
    }
}
