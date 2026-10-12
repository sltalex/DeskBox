using System.Runtime.InteropServices;

namespace DeskBox.Platform;

internal static class SystemMonitorPdh
{
    public static bool TryOpen(out nint query) => PdhOpenQuery(null, 0, out query) == 0;
    public static bool TryAddEnglishCounter(nint query, string path, out nint counter) =>
        PdhAddEnglishCounter(query, path, 0, out counter) == 0;
    public static bool TryCollect(nint query) => PdhCollectQueryData(query) == 0;
    public static void Close(nint query) => PdhCloseQuery(query);

    public static double? ReadRate(nint counter)
    {
        if (PdhGetFormattedCounterValue(counter, 0x00000200, nint.Zero, out var value) != 0 ||
            value.Status is not (0 or 1) || !double.IsFinite(value.DoubleValue)) return null;
        return Math.Max(0, value.DoubleValue);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhValue
    {
        public int Status;
        private int _padding;
        public double DoubleValue;
    }

    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, nint userData, out nint query);
    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(nint query, string path, nint userData, out nint counter);
    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(nint counter, uint format, nint type, out PdhValue value);
    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(nint query);
}
