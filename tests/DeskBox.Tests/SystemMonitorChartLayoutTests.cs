using DeskBox.Controls.WidgetContents;

namespace DeskBox.Tests;

public sealed class SystemMonitorChartLayoutTests
{
    [Theory]
    [InlineData(120)]
    [InlineData(214)]
    [InlineData(420)]
    public void BarsFillAvailableWidthWithoutCrossingTheRightEdge(double width)
    {
        const int samples = 60;
        foreach (bool paired in new[] { false, true })
        {
            var (slot, barWidth, gap) = SystemMonitorChartLayout.BarMetrics(width, samples, paired);
            Assert.Equal(width / samples, slot, 6);
            Assert.True(barWidth > 0);
            double lastRight = (samples - 1) * slot +
                (paired ? 2 * barWidth + gap : barWidth);
            Assert.InRange(width - lastRight, 0, 1);
        }
    }

    [Theory]
    [InlineData(120)]
    [InlineData(214)]
    [InlineData(420)]
    public void LineUsesTheFullAvailableWidth(double width)
    {
        const int samples = 30;
        Assert.Equal(width - 1,
            SystemMonitorChartLayout.LineStep(width, samples) * (samples - 1), 6);
    }

    [Fact]
    public void InvalidOrUnavailableWidthsDoNotProduceDrawingCoordinates()
    {
        Assert.Equal(0, SystemMonitorChartLayout.UsableWidth(double.NaN));
        Assert.Equal(0, SystemMonitorChartLayout.UsableWidth(double.PositiveInfinity));
        Assert.Equal(0, SystemMonitorChartLayout.UsableWidth(-10));
        Assert.Equal((0d, 0d, 0d), SystemMonitorChartLayout.BarMetrics(0, 60, true));
        Assert.Equal(0, SystemMonitorChartLayout.LineStep(0, 1));
    }

    [Fact]
    public void ChartCanvasStretchesAndRedrawsOnResize()
    {
        string source = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Controls/WidgetContents/SystemMonitorWidgetContent.cs"));
        Assert.Contains("canvas.ActualWidth", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Stretch, IsHitTestVisible = false", source, StringComparison.Ordinal);
        Assert.Contains("_cpuChart.SizeChanged += Chart_SizeChanged", source, StringComparison.Ordinal);
        Assert.Contains("_memoryChart.SizeChanged += Chart_SizeChanged", source, StringComparison.Ordinal);
        Assert.Contains("_networkChart.SizeChanged += Chart_SizeChanged", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChartWidth", source, StringComparison.Ordinal);
    }
}
