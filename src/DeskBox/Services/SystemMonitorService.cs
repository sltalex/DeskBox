using System.Diagnostics;
using System.Net.NetworkInformation;
using DeskBox.Platform;

namespace DeskBox.Services;

/// <summary>One in-memory sampler shared by every visible monitor entry.</summary>
internal sealed class SystemMonitorService
{
    public static SystemMonitorService Instance { get; } = new();

    private readonly Func<CancellationToken, Task>? _testRunner;
    private readonly object _gate = new();
    private readonly List<Action<SystemMonitorSnapshot, IReadOnlyList<SystemMonitorSnapshot>>> _listeners = [];
    private readonly Queue<SystemMonitorSnapshot> _history = new();
    private CancellationTokenSource? _stop;
    private Task _runningTask = Task.CompletedTask;
    private SystemMonitorSnapshot? _latest;
    private int _refreshIntervalSeconds = 2;

    public void SetRefreshInterval(int seconds) =>
        Volatile.Write(ref _refreshIntervalSeconds, FeatureWidgetsSettingsCoordinator.NormalizeRefreshInterval(seconds));

    private SystemMonitorService() { }
    internal SystemMonitorService(Func<CancellationToken, Task> testRunner) => _testRunner = testRunner;
    internal int SubscriberCount { get { lock (_gate) return _listeners.Count; } }

    public IDisposable Subscribe(Action<SystemMonitorSnapshot, IReadOnlyList<SystemMonitorSnapshot>> listener)
    {
        SystemMonitorSnapshot? latest = null;
        SystemMonitorSnapshot[]? history = null;
        lock (_gate)
        {
            _listeners.Add(listener);
            if (_listeners.Count == 1)
            {
                _history.Clear();
                _latest = null;
                _stop = new CancellationTokenSource();
                CancellationToken token = _stop.Token;
                Task previous = _runningTask;
                _runningTask = Task.Run(async () =>
                {
                    await previous.ConfigureAwait(false);
                    if (!token.IsCancellationRequested)
                        await (_testRunner?.Invoke(token) ?? RunAsync(token)).ConfigureAwait(false);
                });
            }
            else if (_latest is not null)
            {
                latest = _latest;
                history = _history.ToArray();
            }
        }
        if (latest is not null)
            Notify(listener, latest, history!);
        return new Subscription(this, listener);
    }

    private void Unsubscribe(Action<SystemMonitorSnapshot, IReadOnlyList<SystemMonitorSnapshot>> listener)
    {
        lock (_gate)
        {
            _listeners.Remove(listener);
            if (_listeners.Count == 0)
            {
                _stop?.Cancel();
                _stop = null;
                _history.Clear();
                _latest = null;
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var diskCounters = new DiskCounters();
        using var temperatures = new TemperatureProbeClient();
        temperatures.Start();
        CpuTimes? lastCpu = null;
        var networkTracker = new NetworkRateTracker();
        long lastTick = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Read all independent sources off the UI thread. A broken source only
                // blanks its own value and is retried on the next tick.
                var now = DateTimeOffset.Now;
                long tick = Stopwatch.GetTimestamp();
                double elapsed = lastTick == 0 ? 0 : Stopwatch.GetElapsedTime(lastTick, tick).TotalSeconds;
                lastTick = tick;

                double? cpu = null;
                try
                {
                    if (SystemMonitorNativeMethods.TryReadCpu(out ulong idle, out ulong kernel, out ulong user))
                    {
                        var current = new CpuTimes(idle, kernel, user);
                        if (lastCpu is { } previous)
                            cpu = SystemMonitorMath.CpuPercentage(previous.Idle, previous.Kernel, previous.User,
                                current.Idle, current.Kernel, current.User);
                        lastCpu = current;
                    }
                }
                catch { lastCpu = null; }

                double? memory = null;
                long? usedMemory = null, totalMemory = null;
                try
                {
                    if (SystemMonitorNativeMethods.TryReadMemory(out ulong total, out ulong available))
                    {
                        memory = SystemMonitorMath.Percentage(total - available, total);
                        usedMemory = (long)total - (long)available;
                        totalMemory = (long)total;
                    }
                }
                catch { /* Only memory is unavailable. */ }

                double? download = null, upload = null;
                try
                {
                    var current = new Dictionary<string, (long Received, long Sent)>();
                    foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (adapter.OperationalStatus != OperationalStatus.Up ||
                            adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                            continue;
                        var stats = adapter.GetIPv4Statistics();
                        current[adapter.Id] = (stats.BytesReceived, stats.BytesSent);
                    }
                    (download, upload) = networkTracker.Sample(current, elapsed);
                }
                catch { networkTracker.Reset(); }

                IReadOnlyList<SystemMonitorDisk> disks;
                try { disks = diskCounters.Sample(elapsed); }
                catch { disks = []; }
                try { temperatures.EnsureRunning(); }
                catch (Exception ex) { App.LogVerbose($"[SystemMonitor] Temperature restart failed: {ex.Message}"); }
                var temp = temperatures.Latest;
                var snapshot = new SystemMonitorSnapshot(now, cpu, memory, usedMemory, totalMemory,
                    download, upload, temp.Cpu, temp.Gpu, temp.Disk, disks);
                Action<SystemMonitorSnapshot, IReadOnlyList<SystemMonitorSnapshot>>[] listeners;
                SystemMonitorSnapshot[] history;
                lock (_gate)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    _latest = snapshot;
                    _history.Enqueue(snapshot);
                    while (_history.Count > 61 ||
                        (_history.Count > 0 && _history.Peek().Timestamp < now.AddSeconds(-60)))
                        _history.Dequeue();
                    history = _history.ToArray();
                    listeners = _listeners.ToArray();
                }
                foreach (var listener in listeners) Notify(listener, snapshot, history);
                int interval = Volatile.Read(ref _refreshIntervalSeconds);
                await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.LogVerbose($"[SystemMonitor] Sampler stopped: {ex.Message}"); }
    }

    private readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

    private static void Notify(Action<SystemMonitorSnapshot, IReadOnlyList<SystemMonitorSnapshot>> listener,
        SystemMonitorSnapshot snapshot, IReadOnlyList<SystemMonitorSnapshot> history)
    {
        try { listener(snapshot, history); }
        catch (Exception ex) { App.LogVerbose($"[SystemMonitor] Listener failed: {ex.Message}"); }
    }

    private sealed class Subscription(SystemMonitorService owner,
        Action<SystemMonitorSnapshot, IReadOnlyList<SystemMonitorSnapshot>> listener) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Unsubscribe(listener);
        }
    }

}
