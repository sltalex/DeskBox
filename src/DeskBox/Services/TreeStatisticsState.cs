namespace DeskBox.Services;

internal readonly record struct TreeStatisticsState(bool ToggleEnabled, bool ToggleOn, string OffContentKey)
{
    public static TreeStatisticsState FromProgress(bool treeEnabled, bool paused) =>
        new(treeEnabled, treeEnabled && !paused,
            treeEnabled ? "Tree.Settings.StatisticsPaused" : "Tree.Settings.StatisticsDisabled");

    public static bool PausedFromToggle(bool isOn) => !isOn;
}
