namespace DeskBox.Services;

internal sealed record SystemMonitorDisk(
    string Name, double? UsedPercent, long? TotalBytes, long? FreeBytes,
    double? ReadBytesPerSecond, double? WriteBytesPerSecond);

internal sealed record SystemMonitorSnapshot(
    DateTimeOffset Timestamp, double? CpuPercent, double? MemoryPercent,
    long? UsedMemoryBytes, long? TotalMemoryBytes,
    double? DownloadBytesPerSecond, double? UploadBytesPerSecond,
    double? CpuTemperature, double? GpuTemperature, double? DiskTemperature,
    IReadOnlyList<SystemMonitorDisk> Disks);
