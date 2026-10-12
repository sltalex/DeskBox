namespace DeskBox.Services;

internal sealed class NetworkRateTracker
{
    private Dictionary<string, (long Received, long Sent)> _previous = [];

    public (double? Download, double? Upload) Sample(
        IReadOnlyDictionary<string, (long Received, long Sent)> current, double elapsedSeconds)
    {
        double download = 0, upload = 0;
        bool valid = false;
        foreach (var (id, bytes) in current)
        {
            if (!_previous.TryGetValue(id, out var old)) continue;
            var rx = SystemMonitorMath.BytesPerSecond(old.Received, bytes.Received, elapsedSeconds);
            var tx = SystemMonitorMath.BytesPerSecond(old.Sent, bytes.Sent, elapsedSeconds);
            if (!rx.HasValue || !tx.HasValue) continue;
            download += rx.Value;
            upload += tx.Value;
            valid = true;
        }
        _previous = new Dictionary<string, (long Received, long Sent)>(current);
        return valid ? (download, upload) : (null, null);
    }

    public void Reset() => _previous.Clear();
}
