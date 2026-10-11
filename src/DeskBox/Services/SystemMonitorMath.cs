namespace DeskBox.Services;

internal static class SystemMonitorMath
{
    public static double? Percentage(ulong used, ulong total) =>
        total == 0 ? null : Math.Clamp(100d * used / total, 0, 100);

    public static double? CpuPercentage(ulong previousIdle, ulong previousKernel, ulong previousUser,
        ulong idle, ulong kernel, ulong user)
    {
        if (idle < previousIdle || kernel < previousKernel || user < previousUser) return null;
        ulong total = (kernel - previousKernel) + (user - previousUser);
        if (total == 0) return null;
        ulong busy = total > idle - previousIdle ? total - (idle - previousIdle) : 0;
        return Percentage(busy, total);
    }

    public static double? BytesPerSecond(long previous, long current, double elapsedSeconds)
    {
        // A replaced adapter or reset counter is a new baseline, not a traffic spike.
        if (previous < 0 || current < 0 || elapsedSeconds <= 0) return null;
        if (current >= previous) return (current - previous) / elapsedSeconds;
        // Some interface providers expose 32-bit counters through a long. Only
        // treat a near-maximum -> near-zero transition as rollover; other
        // decreases are adapter resets and establish a fresh baseline.
        if (previous >= 0xF0000000L && previous <= uint.MaxValue && current <= 0x0FFFFFFFL)
            return ((double)uint.MaxValue + 1 - previous + current) / elapsedSeconds;
        return null;
    }

    public static string FormatRate(double? bytesPerSecond)
    {
        if (bytesPerSecond is null || !double.IsFinite(bytesPerSecond.Value)) return "—";
        double value = Math.Max(0, bytesPerSecond.Value);
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}
