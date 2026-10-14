using System.Net;
using System.Text;
using System.Text.Json;
using DeskBox.Contracts;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class TreeCloudSyncServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DeskBoxTreeCloudTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FailedUpload_PersistsAndReusesSameOperationId()
    {
        var auth = new FakeAuth("user-1", "Simon");
        var offlineApi = new FakeApi { Failure = new HttpRequestException("offline") };
        TreeProgress progress = Progress(120, 40, 3, 1);
        await using (var first = new TreeCloudSyncService(offlineApi, auth, () => progress, _root))
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => first.SyncNowAsync());
        }
        string firstOperation = ReadOperation(Assert.Single(offlineApi.Bodies));

        var onlineApi = new FakeApi();
        await using (var second = new TreeCloudSyncService(onlineApi, auth, () => progress, _root))
        {
            Assert.True(await second.SyncNowAsync());
            Assert.False(await second.SyncNowAsync());
        }

        Assert.Equal(firstOperation, ReadOperation(Assert.Single(onlineApi.Bodies)));
    }

    [Fact]
    public async Task LocalReset_RebaselinesWithoutNegativeOrDuplicateUpload()
    {
        var auth = new FakeAuth("user-1", "Simon");
        var api = new FakeApi();
        TreeProgress progress = Progress(100, 100, 2, 1);
        await using var service = new TreeCloudSyncService(api, auth, () => progress, _root);
        Assert.True(await service.SyncNowAsync());

        progress = Progress(10, 10, 0, 0);
        Assert.False(await service.SyncNowAsync());
        progress = Progress(15, 15, 0, 0);
        Assert.True(await service.SyncNowAsync());

        using JsonDocument body = JsonDocument.Parse(api.Bodies[^1]);
        Assert.Equal(5, body.RootElement.GetProperty("energy_delta").GetInt64());
        Assert.Equal(5, body.RootElement.GetProperty("day_growth_delta").GetInt64());
    }

    private static TreeProgress Progress(long energy, long today, long inventory, long harvested) => new()
    {
        LocalDay = "2026-10-14", TotalEnergy = energy, TodayKeys = today,
        FruitInventory = inventory, HarvestedFruit = harvested
    };

    private static string ReadOperation(byte[] json)
    {
        using JsonDocument body = JsonDocument.Parse(json);
        return body.RootElement.GetProperty("operation_id").GetString()!;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeAuth(string id, string username) : IAuthSession
    {
        public event Action? SessionChanged;
        public bool IsAuthenticated => true;
        public AuthUser? CurrentUser { get; } = new(id, username);
        public Task<AuthUser> RegisterAsync(string username, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthUser> SignInAsync(string username, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeApi : IAuthenticatedAccountApi
    {
        internal Exception? Failure { get; set; }
        internal List<byte[]> Bodies { get; } = [];
        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativePath, byte[]? jsonBody, string? contentEncoding, CancellationToken cancellationToken = default)
        {
            if (jsonBody is not null) Bodies.Add(jsonBody);
            if (Failure is not null) throw Failure;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"duplicate\":false,\"profile\":{}}", Encoding.UTF8, "application/json")
            });
        }
    }
}
