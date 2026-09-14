namespace OpenRpg.Tests;

// A minimal "does this project even compile and run against the real
// game source" check — if this fails, every other test file here is
// moot regardless of what it actually asserts.
public class SmokeTests
{
    [Fact]
    public void CanConstructAGameAction()
    {
        var action = new GameAction("wait");
        Assert.Equal("wait", action.Id);
    }
}
