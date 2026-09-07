// Global, session-wide toggles that don't belong to any one NPC or the
// world's spatial state — same decoupled "static, not threaded through
// every constructor" pattern as WorldRegistry/PathGrid/SpeechLog/
// WorldExploration. Set once at boot from MindConfig (see Main._Ready())
// and not meant to change mid-session.
public static class GameSettings
{
    // Off by default: Health hitting 0 knocks a character out — they
    // become incapacitated for a while, then recover at partial health,
    // the same shape Sleep() already restores Fatigue/Health through.
    // On: Health hitting 0 is real — an NPC is permanently removed from
    // the world, the player gets a real game-over/restart. Both paths
    // are fully implemented (see NPCActor's Incapacitated state and
    // Main's death handling); this just picks which one actually fires.
    public static bool PermadeathEnabled = false;
}
