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

    [Fact]
    public async Task SyncAsync_UsesHistoryFirstThenOverlappingIncrementalWindowAndMergesCache()
    {
        string root = CreateTempRoot();
        try
        {
            var dws = new FakeDwsClient();
            dws.Pages.Enqueue(new DingTalkGroupMessagePage(
                [new("a", "研发群", "[旧文档](https://alidocs.dingtalk.com/i/old)", DateTimeOffset.Now.AddDays(-3), false)],
                false, null));
            var service = new DingTalkGroupDocumentService(dws, root);
            var progress = new List<DingTalkGroupDocumentSyncProgress>();
            service.ProgressChanged += progress.Add;

            await service.SyncAsync();
            Assert.True(service.HasCompletedInitialSync);
            Assert.Equal(6, service.SyncIntervalHours);
            Assert.True(dws.Requests[0].Start < DateTimeOffset.Now.AddYears(-9));
            Assert.Contains(progress, item => item.IsInitialSync && item.IsRunning && item.TotalSegments > 1);
            DateTimeOffset firstCompleted = Assert.IsType<DateTimeOffset>(service.LastSyncCompletedAt);

            dws.Requests.Clear();
            dws.Pages.Enqueue(new DingTalkGroupMessagePage(
                [new("b", "研发群", "[新文档](https://alidocs.dingtalk.com/i/new)", DateTimeOffset.Now, false)],
                false, null));
            await service.SyncAsync();

            Assert.InRange(dws.Requests[0].Start, firstCompleted.AddDays(-2).AddMinutes(-1), firstCompleted.AddDays(-2).AddMinutes(1));
            Assert.Equal(2, service.Documents.Count);
            Assert.Contains(service.Documents, item => item.Url.EndsWith("/old", StringComparison.Ordinal));
            Assert.Contains(service.Documents, item => item.Url.EndsWith("/new", StringComparison.Ordinal));
            Assert.Contains(progress, item => !item.IsInitialSync && item.IsRunning);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task SyncAsync_IncompletePaginationDoesNotReplaceSavedIndexOrAdvanceCheckpoint()
    {
        string root = CreateTempRoot();
        try
        {
            var dws = new FakeDwsClient();
            dws.Pages.Enqueue(new DingTalkGroupMessagePage(
                [new("a", "研发群", "https://alidocs.dingtalk.com/i/kept", DateTimeOffset.Now, false)],
                false, null));
            var service = new DingTalkGroupDocumentService(dws, root);
            await service.SyncAsync();
            DateTimeOffset? completed = service.LastSyncCompletedAt;

            dws.Pages.Enqueue(new DingTalkGroupMessagePage(
                [new("b", "研发群", "https://alidocs.dingtalk.com/i/lost", DateTimeOffset.Now, false)],
                true, null));
            await Assert.ThrowsAsync<InvalidDataException>(() => service.SyncAsync());

            Assert.Equal(completed, service.LastSyncCompletedAt);
            Assert.Single(service.Documents);
            Assert.EndsWith("/kept", service.Documents[0].Url, StringComparison.Ordinal);
            var reloaded = new DingTalkGroupDocumentService(dws, root);
            Assert.Single(await reloaded.LoadAsync());
            Assert.Equal(completed, reloaded.LastSyncCompletedAt);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task SetSyncIntervalHoursAsync_PersistsForTheNextServiceInstance()
    {
        string root = CreateTempRoot();
        try
        {
            var dws = new FakeDwsClient();
            var first = new DingTalkGroupDocumentService(dws, root);
            await first.SetSyncIntervalHoursAsync(12);
            var second = new DingTalkGroupDocumentService(dws, root);
            Assert.Equal(12, second.SyncIntervalHours);
            await second.LoadAsync();
            Assert.Equal(12, second.SyncIntervalHours);
        }
        finally { DeleteTempRoot(root); }
    }

    [Fact]
    public async Task SyncAsync_DwsIdentityChangeDuringScanDoesNotCommitMixedDocuments()
    {
        string root = CreateTempRoot();
        try
        {
            var dws = new FakeDwsClient();
            dws.Pages.Enqueue(new DingTalkGroupMessagePage(
                [new("a", "研发群", "https://alidocs.dingtalk.com/i/kept", DateTimeOffset.Now, false)],
                false, null));
            var service = new DingTalkGroupDocumentService(dws, root);
            await service.SyncAsync();
            DateTimeOffset? checkpoint = service.LastSyncCompletedAt;

            dws.Pages.Enqueue(new DingTalkGroupMessagePage(
                [new("b", "其他群", "https://alidocs.dingtalk.com/i/mixed", DateTimeOffset.Now, false)],
                false, null));
            dws.SwitchAccountOnNextMessagePage = true;
            await Assert.ThrowsAsync<DingTalkDwsAuthorizationRequiredException>(() => service.SyncAsync());

            Assert.Equal(checkpoint, service.LastSyncCompletedAt);
            Assert.Single(service.Documents);
            dws.UserId = "account-a";
            var reloaded = new DingTalkGroupDocumentService(dws, root);
            Assert.Single(await reloaded.LoadAsync());
        }
        finally { DeleteTempRoot(root); }
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
        public bool SwitchAccountOnNextMessagePage { get; set; }
        public Queue<DingTalkGroupMessagePage> Pages { get; } = new();
        public List<(DateTimeOffset Start, DateTimeOffset End)> Requests { get; } = [];

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
            Requests.Add((start, end));
            if (SwitchAccountOnNextMessagePage)
            {
                UserId = "account-b";
                SwitchAccountOnNextMessagePage = false;
            }
            DingTalkGroupMessagePage page = Pages.Count > 0
                ? Pages.Dequeue()
                : new DingTalkGroupMessagePage([], false, null);
            return Task.FromResult(page);
        }
    }
}
