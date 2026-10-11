using System.Diagnostics;
using System.Globalization;

namespace DeskBox.Services;

internal sealed class TemperatureProbeClient : IDisposable
{
    private Process? _process;
    private CancellationTokenSource? _stop;
    private DateTimeOffset _lastAttempt;
    private DateTimeOffset _lastSample;
    private (double? Cpu, double? Gpu, double? Disk) _latest;
    private readonly object _gate = new();
    private int _generation;

    public (double? Cpu, double? Gpu, double? Disk) Latest
    {
        get
        {
            lock (_gate)
                return DateTimeOffset.UtcNow - _lastSample <= TimeSpan.FromSeconds(6)
                    ? _latest : default;
        }
    }

    public void Start() => EnsureRunning();

    public void EnsureRunning()
    {
        // Hardware discovery may take longer than one sampling interval. Allow
        // a fresh process time to produce its first line before treating it as stuck.
        if (_process is { HasExited: false } &&
            DateTimeOffset.UtcNow - (_lastSample > _lastAttempt ? _lastSample : _lastAttempt)
                <= TimeSpan.FromSeconds(6)) return;
        lock (_gate) _latest = default;
        if (_process is { HasExited: false })
        {
            try { _process.Kill(); } catch { }
        }
        if (DateTimeOffset.UtcNow - _lastAttempt < TimeSpan.FromSeconds(10)) return;
        _lastAttempt = DateTimeOffset.UtcNow;
        int generation = Interlocked.Increment(ref _generation);
        _process?.Dispose();
        _stop?.Dispose();
        string executable = Path.Combine(AppContext.BaseDirectory, "TemperatureProbe", "DeskBox.TemperatureProbe.exe");
        if (!File.Exists(executable)) return;
        try
        {
            _stop = new CancellationTokenSource();
            _process = Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = false
            });
            if (_process is not null) _ = ReadAsync(_process, _stop.Token, generation);
        }
        catch (Exception ex)
        {
            lock (_gate) _latest = default;
            App.LogVerbose($"[SystemMonitor] Temperature probe unavailable: {ex.Message}");
        }
    }

    private async Task ReadAsync(Process process, CancellationToken stop, int generation)
    {
        try
        {
            while (!stop.IsCancellationRequested && !process.HasExited)
            {
                string? line = await process.StandardOutput.ReadLineAsync(stop).ConfigureAwait(false);
                if (line is null) break;
                var parts = line.Split('|');
                if (parts.Length != 3) continue;
                lock (_gate)
                {
                    if (generation == _generation)
                    {
                        _latest = (Parse(parts[0]), Parse(parts[1]), Parse(parts[2]));
                        _lastSample = DateTimeOffset.UtcNow;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException) { }
        finally { lock (_gate) { if (generation == _generation) _latest = default; } }
    }

    private static double? Parse(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) &&
        double.IsFinite(number) ? number : null;

    public void Dispose()
    {
        Interlocked.Increment(ref _generation);
        _stop?.Cancel();
        try { if (_process is { HasExited: false }) _process.Kill(); }
        catch { }
        _process?.Dispose();
        _stop?.Dispose();
    }
}
