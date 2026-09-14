namespace OpenRpg.Tests;

// Shared "every array field populated, nothing offered by default"
// AvailableTargets builder — see MindToolGatingTests' own comment on why
// this is required rather than a bare `new()`: BuildTools calls .Length
// on every array field with no null guard, exactly like every real call
// site (NpcAgent.TakeTurn) always supplies one.
public static class TestTargets
{
    public static Mind.AvailableTargets Empty() => new()
    {
        TreeIds = System.Array.Empty<string>(),
        FishingSpotIds = System.Array.Empty<string>(),
        PineTreeIds = System.Array.Empty<string>(),
        BerryBushIds = System.Array.Empty<string>(),
        TravelTargetIds = System.Array.Empty<string>(),
        NearbyNpcNames = System.Array.Empty<string>(),
        CarriedItems = System.Array.Empty<string>(),
        AnimalIds = System.Array.Empty<string>(),
        StickIds = System.Array.Empty<string>(),
    };
}
