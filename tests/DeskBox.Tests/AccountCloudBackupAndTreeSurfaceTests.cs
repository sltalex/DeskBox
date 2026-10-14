using System.Text.Json;
using DeskBox.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DeskBox.Tests;

public sealed class AccountCloudBackupAndTreeSurfaceTests
{
    [Fact]
    public async Task ServiceRegistry_ResolvesAccountBackupAndTreeServices()
    {
        var services = new ServiceCollection();
        services.AddDeskBoxServices();

        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<AccountCloudBackupService>());
        Assert.NotNull(provider.GetRequiredService<TreeCloudSyncService>());
    }

    [Fact]
    public void CloudBackupPage_ContainsIndependentAccountBackupAndRestoreCards()
    {
        string xaml = File.ReadAllText(TestPaths.FromRepository("src/DeskBox/Views/SettingsWindow.xaml"));
        string section = Slice(xaml, "x:Key=\"CloudBackupSettingsSectionTemplate\"", "x:Key=\"DataHealthSettingsSectionTemplate\"");

        Assert.Contains("x:Name=\"AccountCloudBackupToggle\"", section, StringComparison.Ordinal);
        Assert.Contains("Settings.AccountBackup.Enable.Title", section, StringComparison.Ordinal);
        Assert.Contains("Settings.AccountBackup.Restore.Title", section, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AccountCloudBackupSnapshotsList\"", section, StringComparison.Ordinal);
        Assert.Contains("Settings.CloudBackup.Provider.Title", section, StringComparison.Ordinal);
        Assert.Contains("Settings.CloudBackup.Interval.Title", section, StringComparison.Ordinal);
        string codeBehind = File.ReadAllText(TestPaths.FromRepository("src/DeskBox/Views/SettingsWindow.CloudBackup.cs"));
        Assert.Contains("_accountCloudBackupListOffline", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CancelPreparedRestore", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void TreeSettings_UsesCardsAndAotSafeListViewRows()
    {
        string source = File.ReadAllText(TestPaths.FromRepository("src/DeskBox/Views/SettingsSections/TreeSettingsSection.cs"));

        Assert.True(source.Split("new() { HorizontalContentAlignment", StringSplitOptions.None).Length - 1 >= 4);
        Assert.Contains("ListView _leaderboard", source, StringComparison.Ordinal);
        Assert.Contains("new Grid", source, StringComparison.Ordinal);
        Assert.Contains("🥇", source, StringComparison.Ordinal);
        Assert.Contains("Tree.Settings.Leaderboard.Me", source, StringComparison.Ordinal);
        Assert.Contains("Tree.Settings.Leaderboard.CurrentRank", source, StringComparison.Ordinal);
        Assert.Contains("AddStageCell", source, StringComparison.Ordinal);
        Assert.Contains("LightGreen", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DataGrid", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryLanguage_DefinesAccountBackupAndTreeLeaderboardKeys()
    {
        string[] keys =
        [
            "Settings.AccountBackup.Enable.Title", "Settings.AccountBackup.Restore.Title",
            "Settings.AccountBackup.SignInRequired.Body", "Settings.AccountBackup.RestoreConfirm.Body",
            "Settings.AccountBackup.Status.LastSuccess", "Settings.AccountBackup.Status.LastFailure",
            "Settings.AccountBackup.Status.Never",
            "Tree.Settings.Card.Current", "Tree.Settings.Card.Switches", "Tree.Settings.Card.Progress",
            "Tree.Settings.Card.Leaderboard", "Tree.Settings.Leaderboard.Participate",
            "Tree.Settings.Leaderboard.SignIn", "Tree.Settings.Leaderboard.Offline", "Tree.Settings.Leaderboard.Me",
            "Tree.Settings.Leaderboard.CurrentRank"
        ];
        foreach (string file in Directory.EnumerateFiles(TestPaths.FromRepository("src/DeskBox/Strings"), "*.json"))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
            foreach (string key in keys)
                Assert.True(document.RootElement.TryGetProperty(key, out _), $"{Path.GetFileName(file)} misses {key}");
        }

        using JsonDocument chinese = JsonDocument.Parse(File.ReadAllText(TestPaths.FromRepository("src/DeskBox/Strings/zh-CN.json")));
        Assert.Equal("诊断与备份", chinese.RootElement.GetProperty("Settings.Nav.Maintenance").GetString());
        Assert.Equal("诊断与备份", chinese.RootElement.GetProperty("Settings.Section.Maintenance").GetString());
        Assert.Equal("开启云帐户备份", chinese.RootElement.GetProperty("Settings.AccountBackup.Enable.Title").GetString());
    }

    private static string Slice(string value, string start, string end)
    {
        int startIndex = value.IndexOf(start, StringComparison.Ordinal);
        int endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return value[startIndex..endIndex];
    }
}
