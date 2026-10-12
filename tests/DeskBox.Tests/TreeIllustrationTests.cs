using DeskBox.Controls.WidgetContents;
using System.Globalization;
using System.Xml.Linq;

namespace DeskBox.Tests;

public sealed class TreeIllustrationTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    [Theory]
    [InlineData(int.MinValue, "stage-1.svg")]
    [InlineData(0, "stage-1.svg")]
    [InlineData(1, "stage-1.svg")]
    [InlineData(2, "stage-2.svg")]
    [InlineData(3, "stage-3.svg")]
    [InlineData(4, "stage-4.svg")]
    [InlineData(5, "stage-5.svg")]
    [InlineData(6, "stage-5.svg")]
    [InlineData(int.MaxValue, "stage-5.svg")]
    public void EachGrowthStageSelectsItsBundledIllustration(int stage, string expected)
    {
        Assert.Equal(expected, TreeIllustrationLayout.StageAssetName(stage));
        Assert.True(File.Exists(AssetPath(expected)));
    }

    [Theory]
    [InlineData(long.MinValue, 0, 0L)]
    [InlineData(-1L, 0, 0L)]
    [InlineData(0L, 0, 0L)]
    [InlineData(1L, 1, 0L)]
    [InlineData(11L, 11, 0L)]
    [InlineData(12L, 12, 0L)]
    [InlineData(13L, 12, 1L)]
    [InlineData(1000L, 12, 988L)]
    [InlineData(long.MaxValue, 12, long.MaxValue - 12)]
    public void FruitVisualLimitNeverLosesTheOverflowCount(long inventory, int visible, long extra)
    {
        Assert.Equal(visible, TreeIllustrationLayout.VisibleFruitCount(inventory));
        Assert.Equal(extra, TreeIllustrationLayout.ExtraFruitCount(inventory));
    }

    [Fact]
    public void EveryFruitSlotIsDistinctAndInsideTheHarvestableCanopy()
    {
        Assert.Equal(260, TreeIllustrationLayout.Width);
        Assert.Equal(220, TreeIllustrationLayout.Height);
        Assert.Equal(12, TreeIllustrationLayout.MaxVisibleFruit);
        Assert.Equal(TreeIllustrationLayout.MaxVisibleFruit, TreeIllustrationLayout.FruitPositions.Count);
        Assert.Equal(TreeIllustrationLayout.FruitPositions.Count, TreeIllustrationLayout.FruitPositions.Distinct().Count());

        foreach ((double x, double y) in TreeIllustrationLayout.FruitPositions)
        {
            Assert.True(double.IsFinite(x) && double.IsFinite(y));
            Assert.InRange(x, 0, TreeIllustrationLayout.Width - 24);
            Assert.InRange(y, 0, TreeIllustrationLayout.Height - 28);
            Assert.True(TreeIllustrationLayout.CanHarvest(5, 1, x + 12, y + 14));
        }
    }

    [Theory]
    [InlineData(5, 1L, 32, 12, true)]
    [InlineData(5, 1L, 228, 150, true)]
    [InlineData(5, long.MaxValue, 130, 80, true)]
    [InlineData(1, 1L, 130, 80, false)]
    [InlineData(4, 1L, 130, 80, false)]
    [InlineData(6, 1L, 130, 80, false)]
    [InlineData(5, 0L, 130, 80, false)]
    [InlineData(5, -1L, 130, 80, false)]
    [InlineData(5, 1L, 31.99, 80, false)]
    [InlineData(5, 1L, 228.01, 80, false)]
    [InlineData(5, 1L, 130, 11.99, false)]
    [InlineData(5, 1L, 130, 150.01, false)]
    [InlineData(5, 1L, double.NaN, 80, false)]
    [InlineData(5, 1L, 130, double.PositiveInfinity, false)]
    public void HarvestRequiresMaturityInventoryAndACanopyTap(int stage, long inventory, double x, double y, bool expected)
    {
        Assert.Equal(expected, TreeIllustrationLayout.CanHarvest(stage, inventory, x, y));
    }

    [Theory]
    [InlineData("stage-1.svg", "0 0 260 220")]
    [InlineData("stage-2.svg", "0 0 260 220")]
    [InlineData("stage-3.svg", "0 0 260 220")]
    [InlineData("stage-4.svg", "0 0 260 220")]
    [InlineData("stage-5.svg", "0 0 260 220")]
    [InlineData("stage-1-canopy.svg", "0 0 260 220")]
    [InlineData("stage-2-canopy.svg", "0 0 260 220")]
    [InlineData("stage-3-canopy.svg", "0 0 260 220")]
    [InlineData("stage-4-canopy.svg", "0 0 260 220")]
    [InlineData("stage-5-canopy.svg", "0 0 260 220")]
    [InlineData("ground.svg", "0 0 260 220")]
    [InlineData("fruit.svg", "0 0 24 28")]
    [InlineData("falling-leaf.svg", "0 0 18 12")]
    public void IllustrationsAreSelfContainedSvgWithoutUnsupportedEffects(string fileName, string viewBox)
    {
        XDocument document = XDocument.Load(AssetPath(fileName));
        XElement root = Assert.IsType<XElement>(document.Root);
        Assert.Equal(Svg + "svg", root.Name);
        Assert.Equal(viewBox, (string?)root.Attribute("viewBox"));

        string[] unsupportedElements = ["filter", "script", "image", "foreignObject", "text", "animate", "animateTransform"];
        Assert.DoesNotContain(root.Descendants(), element => unsupportedElements.Contains(element.Name.LocalName));

        string[] ids = root.DescendantsAndSelf().Attributes("id").Select(attribute => attribute.Value).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());

        foreach (XAttribute attribute in root.DescendantsAndSelf().Attributes())
        {
            Assert.False(attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase),
                $"Unexpected SVG event handler in {fileName}: {attribute.Name}");
            Assert.NotEqual("filter", attribute.Name.LocalName);
            if (attribute.Name.LocalName == "href")
            {
                Assert.StartsWith("#", attribute.Value, StringComparison.Ordinal);
                Assert.Contains(attribute.Value[1..], ids);
            }

            if (attribute.Value.StartsWith("url(", StringComparison.Ordinal))
            {
                Assert.StartsWith("url(#", attribute.Value, StringComparison.Ordinal);
                Assert.EndsWith(")", attribute.Value, StringComparison.Ordinal);
                Assert.Contains(attribute.Value[5..^1], ids);
            }
        }

        foreach (XElement gradient in root.Descendants().Where(element =>
            element.Name == Svg + "linearGradient" || element.Name == Svg + "radialGradient"))
        {
            XElement[] stops = gradient.Elements(Svg + "stop").ToArray();
            Assert.True(stops.Length >= 2, $"Gradient in {fileName} needs at least two color stops.");
            double previous = -1;
            for (int index = 0; index < stops.Length; index++)
            {
                string? specifiedOffset = (string?)stops[index].Attribute("offset");
                if (index > 0)
                    Assert.NotNull(specifiedOffset);
                string offset = specifiedOffset ?? "0";
                double value = offset.EndsWith('%')
                    ? double.Parse(offset[..^1], CultureInfo.InvariantCulture) / 100
                    : double.Parse(offset, CultureInfo.InvariantCulture);
                Assert.InRange(value, 0, 1);
                Assert.True(value >= previous, $"Gradient offsets are out of order in {fileName}.");
                previous = value;
            }
        }
    }

    [Fact]
    public void FiveStagesHaveTheirOwnGeometryAndShadedForms()
    {
        var geometries = new HashSet<string>(StringComparer.Ordinal);
        for (int stage = 1; stage <= 5; stage++)
        {
            XDocument document = XDocument.Load(AssetPath(TreeIllustrationLayout.StageAssetName(stage)));
            XElement[] paths = document.Descendants(Svg + "path").ToArray();
            Assert.NotEmpty(paths);
            Assert.True(geometries.Add(string.Join("|", paths.Select(path => (string?)path.Attribute("d")))),
                $"Stage {stage} reuses another stage's silhouette.");
            Assert.Contains(document.Descendants(), element =>
                element.Name == Svg + "linearGradient" || element.Name == Svg + "radialGradient");
        }
        Assert.Equal(5, geometries.Count);
    }

    [Fact]
    public void EveryStageHasSeparateFoliageWithoutMovingTheTrunkOrGround()
    {
        for (int stage = 1; stage <= 5; stage++)
        {
            XDocument trunk = XDocument.Load(AssetPath(TreeIllustrationLayout.StageAssetName(stage)));
            XDocument canopy = XDocument.Load(AssetPath(TreeIllustrationLayout.CanopyAssetName(stage)));
            string trunkFill = stage == 1 ? "url(#stem)" : "url(#wood)";
            Assert.Contains(trunk.Descendants(), element => (string?)element.Attribute("fill") == trunkFill);
            Assert.DoesNotContain(canopy.Descendants(), element => (string?)element.Attribute("fill") == trunkFill);
            Assert.Contains(canopy.Descendants(Svg + "path"), element => element.Attribute("d") is not null);
            Assert.InRange(TreeIllustrationLayout.CanopyPivotY(stage), 128, 145);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(5, 3)]
    [InlineData(6, 0)]
    public void FallingLeavesStayBoundedAndFinishWithinOneSharedCycle(int stage, int count)
    {
        IReadOnlyList<FallingLeaf> leaves = TreeIllustrationLayout.FallingLeaves(stage);
        Assert.Equal(count, leaves.Count);
        foreach (FallingLeaf leaf in leaves)
        {
            Assert.InRange(leaf.X - Math.Abs(leaf.Drift), 12, TreeIllustrationLayout.Width - 30);
            Assert.InRange(leaf.X + Math.Abs(leaf.Drift), 12, TreeIllustrationLayout.Width - 30);
            Assert.InRange(leaf.Y, 12, 150);
            Assert.InRange(leaf.DelaySeconds, 0, TreeIllustrationLayout.LeafCycleSeconds);
            Assert.InRange(leaf.DurationSeconds, 2, TreeIllustrationLayout.LeafCycleSeconds);
            Assert.True(leaf.DelaySeconds + leaf.DurationSeconds <= TreeIllustrationLayout.LeafCycleSeconds);
        }
    }

    [Fact]
    public void TreeAssetsAreCopiedIntoTheApplicationOutput()
    {
        XDocument project = XDocument.Load(TestPaths.FromRepository("src/DeskBox/DeskBox.csproj"));
        XElement content = Assert.Single(project.Descendants("Content").Where(element =>
            ((string?)element.Attribute("Include"))?.Replace('\\', '/') == "Assets/Tree/*.svg"));
        Assert.Equal("PreserveNewest", (string?)content.Element("CopyToOutputDirectory"));
        Assert.Equal("PreserveNewest", (string?)content.Element("CopyToPublishDirectory"));
    }

    private static string AssetPath(string fileName) =>
        TestPaths.FromRepository($"src/DeskBox/Assets/Tree/{fileName}");

    [Fact]
    public void TreeMotionUsesSharedAccessibilityPolicyAndStopsWithTheView()
    {
        string source = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Controls/WidgetContents/TreeWidgetContent.cs"));
        Assert.Contains("!_disposed && _visible && IsLoaded && WindowsCompatibilityService.ShouldAnimate", source);
        Assert.Contains("Unloaded += (_, _) => { _counter.Changed -= CounterChanged; _timer.Stop(); StopMotion(); };", source);
        Assert.Contains("else { _timer.Stop(); StopMotion(); }", source);
        Assert.Contains("_sway.Stop();", source);
        Assert.Contains("_celebration.Stop();", source);
        Assert.Contains("_fallingLeaves.Stop();", source);
        Assert.Contains("_fallingLeafLayer.Visibility = Visibility.Collapsed;", source);
        Assert.Contains("Storyboard.SetTarget(sway, _canopyTransform);", source);
        Assert.DoesNotContain("new UISettings()", source);
    }

    [Fact]
    public void TreeProgressPercent_SitsOutsideTheRoundedGrowthTrack()
    {
        string source = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Controls/WidgetContents/TreeWidgetContent.cs"));
        Assert.Contains("CornerRadius = new CornerRadius(7)", source, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(_progress, 1)", source, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(_percent, 2)", source, StringComparison.Ordinal);
    }
}
