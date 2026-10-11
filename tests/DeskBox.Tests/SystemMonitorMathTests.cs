using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class SystemMonitorMathTests
{
    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(50, 100, 50d)]
    [InlineData(150, 100, 100d)]
    public void Percentage_HandlesMissingAndClamps(ulong used, ulong total, double? expected) =>
        Assert.Equal(expected, SystemMonitorMath.Percentage(used, total));

    [Fact]
    public void CpuPercentage_SubtractsIdleFromKernelAndRejectsReset()
    {
        Assert.Equal(75, SystemMonitorMath.CpuPercentage(10, 30, 10, 15, 45, 15));
        Assert.Null(SystemMonitorMath.CpuPercentage(10, 30, 10, 9, 45, 15));
    }

    [Fact]
    public void BytesPerSecond_RejectsCounterResetAndZeroElapsed()
    {
        Assert.Equal(512, SystemMonitorMath.BytesPerSecond(1024, 2048, 2));
        Assert.Null(SystemMonitorMath.BytesPerSecond(2048, 1024, 2));
        Assert.Equal(16, SystemMonitorMath.BytesPerSecond(0xFFFFFFF0, 0x10, 2));
        Assert.Null(SystemMonitorMath.BytesPerSecond(0, 1024, 0));
        Assert.Equal("1 MB/s", SystemMonitorMath.FormatRate(1048576));
    }
}
