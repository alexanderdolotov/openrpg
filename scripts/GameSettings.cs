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

    // On by default: every NPC and animal shows a small floating
    // Health/Fatigue/Hunger readout above its head (see
    // VitalsBarDisplay), not just the player's own VitalsPanel. Meant
    // to actually change mid-session — the in-game Settings menu's
    // checkbox flips this directly, and every existing display picks
    // it up on its own next frame (see VitalsBarDisplay._Process),
    // unlike PermadeathEnabled above which is only ever read once at
    // boot.
    public static bool ShowVitalsBars = true;

    // 1 (default): the normal console/thought-log verbosity — NPC
    // decisions/actions, combat that touches a human, deaths, and
    // everything Main.Log's own callers already produce. 2: also
    // includes ambient wildlife noise that's entirely animal-vs-animal
    // and never involves a human either way — a rabbit fleeing a wolf,
    // or noticing one closing in (see Animal.SetFleeing/Rabbit.
    // NoticesWolf) — real, constant background activity in a live
    // ecosystem that's rarely what anyone watching the console actually
    // wants to see by default, unlike a wolf going after a human (still
    // level 1; see SetChaseOrAttack) or an animal actually dying.
    public static int LogLevel = 1;
}
