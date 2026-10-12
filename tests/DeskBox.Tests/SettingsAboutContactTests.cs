namespace DeskBox.Tests;

public sealed class SettingsAboutContactTests
{
    [Fact]
    public void AboutSection_OmitsPersonalWebsiteFeedbackAndSupportActions()
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(
            root,
            "src/DeskBox/Views/SettingsWindow.xaml"));
        string settingsSearchCatalog = File.ReadAllText(Path.Combine(
            root,
            "src/DeskBox/Services/SettingsSearchCatalog.cs"));

        Assert.DoesNotContain("AboutMeButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AboutWebsiteButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AboutMeDialog", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowAboutMeButton_Click", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenWebsiteButton_Click", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowFeedbackDialogButton_Click", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowMyFeedbackButton_Click", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.About.FeedbackTitle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SupportDeskBoxDialog", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreSupportButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowStoreSupportDialogButton_Click", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.About.StoreSupportTitle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.About.FeedbackTitle", settingsSearchCatalog, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.About.StoreSupportTitle", settingsSearchCatalog, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "src/DeskBox/Views/SettingsWindow.Feedback.cs")));
        Assert.False(File.Exists(Path.Combine(root, "src/DeskBox/Services/FeedbackService.cs")));
        Assert.Contains("Settings.Update.Title", xaml, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                current.FullName,
                "src",
                "DeskBox",
                "DeskBox.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("DeskBox repository root was not found.");
    }
}
