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
    public float Hunger = 100f; // 100 = fully fed, 0 = starving — same shape as Fatigue, see HungerDecayPerSecond

    public const float LowFatigueThreshold = 25f; // below this, perception explicitly says so — the LLM decides what to do about it, nothing forces sleep
    // Above this, sleep isn't offered as an option at all (NPCActor.
    // CanSleep()) — not tired enough for it to make sense regardless of
    // location.
    public const float SleepUnnecessaryThreshold = 70f;
    public const float PassiveDecayPerSecond = 100f / 900f; // ~15 real minutes from full to empty doing nothing but standing around

    // A bit slower than Fatigue's own decay — a meal holds you longer
    // than a day of being awake does — but the same "drops throughout
    // the day, on its own, regardless of what you're doing" shape.
    // Below LowHungerThreshold, perception says so (mirrors
    // LowFatigueThreshold); at 0, real Health starts draining (see
    // StarvationDamagePerSecond) — the same mechanism Animal already
    // used for "can die from starvation," now shared by every living
    // character, not just wild animals.
    public const float HungerDecayPerSecond = 100f / 1200f; // ~20 real minutes from full to empty
    public const float LowHungerThreshold = 40f;
    public const float StarvationDamagePerSecond = 1.5f; // slower than a wild animal's own 4/sec (Animal.StarvationDamagePerSecond) — a human has a lot more Health (100) to lose than most animals do, so "slowly" here still adds up to a real threat over time, not a rounding error

    public bool NeedsSleep => Fatigue < LowFatigueThreshold;
    public bool NeedsFood => Hunger < LowHungerThreshold;
    public bool IsStarving => Hunger <= 0f;

    // True the instant Health bottoms out, whether from a real hit
    // (Damage()) or from starving (DecayOverTime(), once IsStarving) —
    // what NPCActor's own State.Incapacitated (or, under
    // GameSettings.PermadeathEnabled, real death) actually keys off.
    public bool IsIncapacitated => Health <= 0f;

    public void DecayOverTime(float seconds)
    {
        Fatigue = Mathf.Max(0f, Fatigue - PassiveDecayPerSecond * seconds);
        Hunger = Mathf.Max(0f, Hunger - HungerDecayPerSecond * seconds);
        if (IsStarving)
            Health = Mathf.Max(0f, Health - StarvationDamagePerSecond * seconds);
    }

    // Real labor costs more than just existing — called once per
    // resolved gather attempt (success or fumble; the effort was spent
    // either way).
    public void Exert(float amount)
    {
        Fatigue = Mathf.Max(0f, Fatigue - amount);
    }

    // Sleep restores fatigue fully and nudges health back up a little.
    // A full night's rest — restores Health right along with Fatigue,
    // not just a token +10 (a real, previously-underwhelming amount
    // when Health had actually taken a beating). "cannot sleep while
    // in fight mode" and CanSleep()'s own three-tier rule (never when
    // barely tired, only near home unless truly exhausted) are what
    // keep this from being a free, risk-free heal available anytime —
    // once those conditions are actually met, a real rest fixes you up
    // properly, the same way it already fully fixes fatigue. Hunger is
    // deliberately untouched here — you don't eat in your sleep.
    public void Sleep()
    {
        Fatigue = 100f;
        Health = 100f;
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
        string fatigueState = Fatigue <= 0f ? "completely exhausted"
            : NeedsSleep ? "exhausted — needs sleep soon"
            : Fatigue < 60f ? "getting tired"
            : "well-rested";
        string hungerState = IsStarving ? "starving — losing health"
            : NeedsFood ? "hungry — should eat soon"
            : Hunger < 65f ? "getting hungry"
            : "well-fed";
        return $"health {Health:0}/100, fatigue {Fatigue:0}/100 ({fatigueState}), hunger {Hunger:0}/100 ({hungerState})";
    }
}
