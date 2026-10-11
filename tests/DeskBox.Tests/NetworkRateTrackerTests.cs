using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class NetworkRateTrackerTests
{
    [Fact]
    public void FirstSampleDisconnectAndAdapterSwitchRebuildBaselines()
    {
        var tracker = new NetworkRateTracker();
        Assert.Equal((null, null), tracker.Sample(new Dictionary<string, (long, long)> { ["a"] = (100, 200) }, 2));
        Assert.Equal((50d, 25d), tracker.Sample(new Dictionary<string, (long, long)> { ["a"] = (200, 250) }, 2));
        Assert.Equal((null, null), tracker.Sample(new Dictionary<string, (long, long)>(), 2));
        Assert.Equal((null, null), tracker.Sample(new Dictionary<string, (long, long)> { ["b"] = (900, 800) }, 2));
        Assert.Equal((25d, 50d), tracker.Sample(new Dictionary<string, (long, long)> { ["b"] = (950, 900) }, 2));
    }
}
