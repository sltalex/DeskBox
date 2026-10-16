using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class DingTalkGroupDocumentServiceTests
{
    [Fact]
    public async Task RefreshAsync_KeepsSameUrlUnderEachGroupAndUsesLatestOccurrence()
    {
        string root = CreateTempRoot();
        try
        {
            var dws = new FakeDwsClient();
            dws.Pages.Enqueue(new DingTalkGroupMessagePage(
            [
                new("a", "研发群", "[方案](https://alidocs.dingtalk.com/i/shared) https://alidocs.dingtalk.com/i/shared", DateTimeOffset.Parse("2026-10-15T10:00:00+08:00"), false),
                new("b", "运营群", "https://alidocs.dingtalk.com/i/shared", DateTimeOffset.Parse("2026-10-15T11:00:00+08:00"), false),
                new("a", "研发群", "[更新方案](https://alidocs.dingtalk.com/i/shared)", DateTimeOffset.Parse("2026-10-15T12:00:00+08:00"), false)
            ], false, null));

            var service = new DingTalkGroupDocumentService(dws, root);
            IReadOnlyList<DingTalkGroupDocument> documents = await service.RefreshAsync();

            Assert.Equal(2, documents.Count);
            Assert.Equal("更新方案", documents.Single(document => document.GroupName == "研发群").Title);
            Assert.Equal("运营群", documents.Single(document => document.GroupName == "运营群").GroupName);
            Assert.All(documents, document => Assert.Equal("https://alidocs.dingtalk.com/i/shared", document.Url));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task LoadAsync_SeparatesIndexesWhenDwsAccountChanges()
    {
        string root = CreateTempRoot();
        try
        {
            var dws = new FakeDwsClient();
            dws.Pages.Enqueue(new DingTalkGroupMessagePage(
            [new("a", "账号A群", "https://alidocs.dingtalk.com/i/a", DateTimeOffset.Now, false)],
                false,
                null));
            var service = new DingTalkGroupDocumentService(dws, root);
            await service.RefreshAsync();

            dws.UserId = "account-b";
            IReadOnlyList<DingTalkGroupDocument> otherAccount = await service.LoadAsync();

            Assert.Empty(otherAccount);
            Assert.NotEqual("账号A群", otherAccount.FirstOrDefault()?.GroupName);
            Assert.Single(Directory.GetFiles(root, "group-documents-*.json"));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static string CreateTempRoot()
        => Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "DeskBox.Tests", Guid.NewGuid().ToString("N"))).FullName;

    private static void DeleteTempRoot(string root)
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class FakeDwsClient : IDingTalkDwsClient
    {
        public string UserId { get; set; } = "account-a";
        public Queue<DingTalkGroupMessagePage> Pages { get; } = new();

        public Task<DingTalkDwsAuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new DingTalkDwsAuthStatus(
                true, true, true, "corp", "Corp", UserId, UserId));

        public Task<DingTalkProfile> GetCurrentUserAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DingTalkDwsAuthStatus> LoginAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DingTalkGroupMessagePage> ListGroupMessagesAsync(
            DateTimeOffset start,
            DateTimeOffset end,
            string? cursor = null,
            int limit = 50,
            CancellationToken cancellationToken = default)
        {
            DingTalkGroupMessagePage page = Pages.Count > 0
                ? Pages.Dequeue()
                : new DingTalkGroupMessagePage([], false, null);
            return Task.FromResult(page);
        }
    }
}
