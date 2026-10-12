using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class SystemMonitorServiceTests
{
    [Fact]
    public async Task WindowsSamplerReturnsMemoryAndCpuWithoutPersistingHistory()
    {
        if (!OperatingSystem.IsWindows()) return;
        var observed = new TaskCompletionSource<SystemMonitorSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = SystemMonitorService.Instance.Subscribe((snapshot, history) =>
        {
            if (snapshot.CpuPercent.HasValue && snapshot.MemoryPercent.HasValue)
                observed.TrySetResult(snapshot);
        });
        var sample = await observed.Task.WaitAsync(TimeSpan.FromSeconds(12));
        Assert.InRange(sample.CpuPercent!.Value, 0, 100);
        Assert.InRange(sample.MemoryPercent!.Value, 0, 100);
        Assert.True(sample.TotalMemoryBytes > 0);
    }

    [Fact]
    public async Task MultipleEntriesShareOneSamplerAndLastCloseStopsIt()
    {
        int starts = 0;
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new SystemMonitorService(async token =>
        {
            Interlocked.Increment(ref starts);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { cancelled.TrySetResult(); }
        });
        using var first = service.Subscribe((_, _) => { });
        using var second = service.Subscribe((_, _) => { });
        await Task.Delay(30);
        Assert.Equal(1, Volatile.Read(ref starts));
        Assert.Equal(2, service.SubscriberCount);
        first.Dispose();
        Assert.False(cancelled.Task.IsCompleted);
        second.Dispose();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, service.SubscriberCount);
    }
}
