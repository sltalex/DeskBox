using DeskBox.Platform;

namespace DeskBox.Services;

/// <summary>English PDH paths make logical-disk rate counters locale independent.</summary>
internal sealed class DiskCounters : IDisposable
{
    private nint _query;
    private string _driveSignature = "";
    private bool _hasBaseline;
    private readonly Dictionary<string, (nint Read, nint Write)> _counters = [];

    public IReadOnlyList<SystemMonitorDisk> Sample(double elapsedSeconds)
    {
        var drives = DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.Fixed)
            .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string signature = string.Join("|", drives.Select(drive => drive.Name));
        if (signature != _driveSignature)
        {
            Reset();
            _driveSignature = signature;
            if (SystemMonitorPdh.TryOpen(out _query))
            {
                foreach (var drive in drives)
                {
                    string instance = drive.Name.TrimEnd('\\');
                    if (SystemMonitorPdh.TryAddEnglishCounter(_query, $"\\LogicalDisk({instance})\\Disk Read Bytes/sec", out nint read) &&
                        SystemMonitorPdh.TryAddEnglishCounter(_query, $"\\LogicalDisk({instance})\\Disk Write Bytes/sec", out nint write))
                        _counters[drive.Name] = (read, write);
                }
            }
        }
        if (_query != 0)
        {
            if (SystemMonitorPdh.TryCollect(_query)) _hasBaseline = true;
            else _hasBaseline = false;
        }

        var result = new List<SystemMonitorDisk>(drives.Length);
        foreach (var drive in drives)
        {
            double? used = null, read = null, write = null;
            long? total = null, free = null;
            try
            {
                if (drive.IsReady)
                {
                    total = drive.TotalSize;
                    free = drive.TotalFreeSpace;
                    used = SystemMonitorMath.Percentage((ulong)(total.Value - free.Value), (ulong)total.Value);
                }
            }
            catch { /* A removed drive remains visible as unavailable this tick. */ }
            if (_hasBaseline && elapsedSeconds > 0 && _counters.TryGetValue(drive.Name, out var counter))
            {
                read = SystemMonitorPdh.ReadRate(counter.Read);
                write = SystemMonitorPdh.ReadRate(counter.Write);
            }
            result.Add(new SystemMonitorDisk(drive.Name.TrimEnd('\\'), used, total, free, read, write));
        }
        return result;
    }

    private void Reset()
    {
        if (_query != 0) SystemMonitorPdh.Close(_query);
        _query = 0;
        _hasBaseline = false;
        _counters.Clear();
    }

    public void Dispose() => Reset();

}
