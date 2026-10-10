namespace DeskBox.Controls.WidgetContents;

/// <summary>Coordinates shared by the tree artwork, fruit overlays and harvest hit target.</summary>
internal static class TreeIllustrationLayout
{
    public const double Width = 260;
    public const double Height = 220;
    public const int MaxVisibleFruit = 12;

    public static IReadOnlyList<(double X, double Y)> FruitPositions { get; } = Array.AsReadOnly<(double X, double Y)>(
    [
        (90, 83), (157, 87), (120, 33), (57, 61),
        (177, 54), (119, 106), (73, 110), (191, 99),
        (87, 44), (147, 48), (44, 91), (144, 115)
    ]);

    public static string StageAssetName(int stage) => $"stage-{Math.Clamp(stage, 1, 5)}.svg";

    public static int VisibleFruitCount(long inventory) => (int)Math.Clamp(inventory, 0, MaxVisibleFruit);

    public static long ExtraFruitCount(long inventory) => inventory > MaxVisibleFruit ? inventory - MaxVisibleFruit : 0;

    public static bool CanHarvest(int stage, long inventory, double x, double y) =>
        stage == 5 && inventory > 0 && x is >= 32 and <= 228 && y is >= 12 and <= 150;
}
