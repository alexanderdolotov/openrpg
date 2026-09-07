// Interaction distances per action — previously duplicated as magic
// numbers in both Mind.ParseToolCall() (the LLM path) and
// NpcAgent.RandomFallback() (the no-LLM path), which meant resizing the
// world meant hunting down two copies. One source now.
public static class ActionRanges
{
    public const float PickApple = 64f;
    public const float CatchFish = 54f;
    public const float GatherPinecone = 64f; // same close-reach as PickApple — walk up to the tree and pick
    public const float GatherBerry = 54f; // a bush sits lower/smaller than a tree — slightly closer than PickApple, same idea as CatchFish's own tighter range
    public const float Deposit = 74f;
    public const float Travel = 120f; // arrival radius for flagpole destinations — big and vague on purpose, not a precise interaction point
    public const float Follow = 90f; // "walking alongside" distance — deliberately looser than a resource-interaction range
    public const float Trade = 70f; // close, hand-to-hand distance
    public const float Steal = 70f; // same — has to actually be within reach
    public const float SleepNearHome = 100f; // "near home" for NPCActor.CanSleep() — looser than Deposit's precise doorstep range, since lying down doesn't need to be exact
}
