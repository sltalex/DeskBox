using System.Net;
using System.Text;
using DeskBox.Contracts;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class DingTalkGroupDocumentSyncServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DeskBox.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ManualSyncRequiresTheLinkedDwsIdentityAndDoesNotPollTheAccountOnEveryRun()
    {
        var dws = new FakeDwsClient();
        var api = new FakeAccountApi();
        var auth = new FakeAuth();
        var account = new DingTalkAccountService(api);
        var documents = new DingTalkGroupDocumentService(dws, _root);
        await using var sync = new DingTalkGroupDocumentSyncService(documents, dws, account, auth);

        dws.UserId = "other-user";
        Assert.False(await sync.SyncNowAsync());
        Assert.Empty(documents.Documents);
        Assert.Equal(0, dws.MessageReads);

        dws.UserId = "linked-user";
        Assert.True(await sync.SyncNowAsync());
        Assert.Single(documents.Documents);
        Assert.Equal(1, api.IdentityReads);
        int completedReads = dws.MessageReads;
        Assert.True(completedReads > 0);

        dws.UserId = "other-user";
        Assert.False(await sync.SyncNowAsync());
        Assert.Equal(1, api.IdentityReads);
        Assert.Equal(completedReads, dws.MessageReads);
    }

    [Fact]
    public async Task DisposeCancelsAnInFlightManualSyncWithoutLeavingAWorkerRunning()
    {
        var dws = new FakeDwsClient { BlockMessages = true };
        var api = new FakeAccountApi();
        var auth = new FakeAuth();
        var account = new DingTalkAccountService(api);
        var documents = new DingTalkGroupDocumentService(dws, _root);
        var sync = new DingTalkGroupDocumentSyncService(documents, dws, account, auth);

        Task<bool> manual = sync.SyncNowAsync();
        await dws.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await sync.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manual);
        Assert.False(documents.IsSyncing);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeAuth : IAuthSession
    {
        public event Action? SessionChanged;
        public bool IsAuthenticated => true;
        public AuthUser? CurrentUser => new("slt-user", "User");
        public Task<AuthUser> RegisterAsync(string username, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthUser> SignInAsync(string username, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAccountApi : IAuthenticatedAccountApi
    {
        public int IdentityReads { get; private set; }

        public Task<HttpResponseMessage> SendAsync(
            HttpMethod method, string relativePath, byte[]? jsonBody, string? contentEncoding,
            CancellationToken cancellationToken = default)
        {
            if (relativePath != "dingtalk/me.php") throw new NotSupportedException(relativePath);
            IdentityReads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"identity\":{\"corp_id\":\"corp\",\"dingtalk_user_id\":\"linked-user\"}}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeDwsClient : IDingTalkDwsClient
    {
        public string UserId { get; set; } = "linked-user";
        public int MessageReads { get; private set; }
        public bool BlockMessages { get; set; }
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<DingTalkDwsAuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new DingTalkDwsAuthStatus(true, true, true, "corp", "Corp", UserId, UserId));

        public Task<DingTalkProfile> GetCurrentUserAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DingTalkDwsAuthStatus> LoginAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async Task<DingTalkGroupMessagePage> ListGroupMessagesAsync(
            DateTimeOffset start, DateTimeOffset end, string? cursor = null, int limit = 50,
            CancellationToken cancellationToken = default)
        {
            MessageReads++;
            ReadStarted.TrySetResult();
            if (BlockMessages) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new DingTalkGroupMessagePage(
                [new("group", "工作群", "https://alidocs.dingtalk.com/i/document", DateTimeOffset.Now, false)],
                false, null);
        }
    }
}
