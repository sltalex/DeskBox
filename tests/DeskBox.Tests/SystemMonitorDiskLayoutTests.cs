using DeskBox.Controls.WidgetContents;

namespace DeskBox.Tests;

public sealed class SystemMonitorDiskLayoutTests
{
    [Theory]
    [InlineData(0, 4, 1)]
    [InlineData(127, 4, 1)]
    [InlineData(128, 4, 1)]
    [InlineData(260, 4, 2)]
    [InlineData(392, 4, 3)]
    [InlineData(800, 2, 2)]
    public void ColumnsFollowAvailableWidgetWidthWithoutExceedingDiskCount(
        double availableWidth, int diskCount, int expectedColumns)
    {
        Assert.Equal(expectedColumns,
            SystemMonitorDiskLayout.ColumnsForWidth(availableWidth, diskCount));
    }
}
