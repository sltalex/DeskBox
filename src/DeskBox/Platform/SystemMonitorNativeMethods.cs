using System.Runtime.InteropServices;

namespace DeskBox.Platform;

internal static class SystemMonitorNativeMethods
{
    public static bool TryReadCpu(out ulong idle, out ulong kernel, out ulong user)
    {
        bool available = GetSystemTimes(out var idleTime, out var kernelTime, out var userTime);
        idle = idleTime.Value;
        kernel = kernelTime.Value;
        user = userTime.Value;
        return available;
    }

    public static bool TryReadMemory(out ulong total, out ulong available)
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        bool success = GlobalMemoryStatusEx(ref status);
        total = status.TotalPhysical;
        available = status.AvailablePhysical;
        return success;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FileTime(uint low, uint high)
    {
        public readonly uint Low = low;
        public readonly uint High = high;
        public ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
