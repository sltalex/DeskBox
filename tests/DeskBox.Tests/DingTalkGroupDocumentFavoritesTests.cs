using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class DingTalkGroupDocumentFavoritesTests
{
    [Fact]
    public async Task FavoriteSurvivesReloadAndSharesIdentityAcrossLinkParameters()
    {
        string root = Path.Combine(Path.GetTempPath(), $"deskbox-dingtalk-favorites-{Guid.NewGuid():N}");
        try
        {
            var dws = new FakeDwsClient();
            var favorites = new DingTalkGroupDocumentFavorites(dws, root);
            const string shared = "https://alidocs.dingtalk.com/i/nodes/document?utm_source=im";
            const string recent = "https://alidocs.dingtalk.com/i/nodes/document?utm_source=portal_recent";

            await favorites.LoadAsync();
            await favorites.SetFavoriteAsync(shared, true);

            var reopened = new DingTalkGroupDocumentFavorites(dws, root);
            await reopened.LoadAsync();
            Assert.True(reopened.IsFavorite(recent));

            await reopened.SetFavoriteAsync(recent, false);
            var afterRemoval = new DingTalkGroupDocumentFavorites(dws, root);
            await afterRemoval.LoadAsync();
            Assert.False(afterRemoval.IsFavorite(shared));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task FavoritesAreScopedToTheCurrentDwsAccount()
    {
        string root = Path.Combine(Path.GetTempPath(), $"deskbox-dingtalk-favorites-{Guid.NewGuid():N}");
        try
        {
            var dws = new FakeDwsClient();
            var favorites = new DingTalkGroupDocumentFavorites(dws, root);
            const string url = "https://alidocs.dingtalk.com/i/nodes/document";
            await favorites.SetFavoriteAsync(url, true);

            dws.UserId = "another-user";
            await favorites.LoadAsync();
            Assert.False(favorites.IsFavorite(url));

            dws.UserId = "first-user";
            await favorites.LoadAsync();
            Assert.True(favorites.IsFavorite(url));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task UnreadableNextAccountDoesNotShowPreviousAccountsFavorites()
    {
        string root = Path.Combine(Path.GetTempPath(), $"deskbox-dingtalk-favorites-{Guid.NewGuid():N}");
        try
        {
            var dws = new FakeDwsClient();
            var favorites = new DingTalkGroupDocumentFavorites(dws, root);
            const string url = "https://alidocs.dingtalk.com/i/nodes/document";
            await favorites.SetFavoriteAsync(url, true);

            dws.UserId = "another-user";
            await favorites.SetFavoriteAsync("https://alidocs.dingtalk.com/i/nodes/another", true);
            string nextAccountFile = Directory.GetFiles(root, "group-document-favorites-*.json")
                .Single(path => File.ReadAllText(path).Contains("/another", StringComparison.Ordinal));
            await File.WriteAllTextAsync(nextAccountFile, "{invalid json");

            dws.UserId = "first-user";
            await favorites.LoadAsync();
            dws.UserId = "another-user";
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => favorites.LoadAsync());
            Assert.False(favorites.IsFavorite(url));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private sealed class FakeDwsClient : IDingTalkDwsClient
    {
        public string UserId { get; set; } = "first-user";

        public Task<DingTalkDwsAuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new DingTalkDwsAuthStatus(true, true, true, "corp", "Company", UserId, "User"));

        public Task<DingTalkProfile> GetCurrentUserAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DingTalkDwsAuthStatus> LoginAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DingTalkGroupMessagePage> ListGroupMessagesAsync(DateTimeOffset start, DateTimeOffset end,
            string? cursor = null, int limit = 50, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string?> GetDocumentTitleAsync(string url, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<DingTalkCalendarEvent>> ListCurrentWeekCalendarAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DingTalkCalendarEvent>>([]);
    }
}
