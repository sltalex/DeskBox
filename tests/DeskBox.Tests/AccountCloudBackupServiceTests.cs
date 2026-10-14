using System.Net;
using System.Text;
using DeskBox.Contracts;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class AccountCloudBackupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DeskBoxAccountCloudServiceTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EnablingRunsImmediatelyThenHonorsIntervalAndPausesWhenSignedOut()
    {
        DateTimeOffset now = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
        var auth = new FakeAuth(new AuthUser("user-1", "Simon"));
        var api = new FakeApi();
        SettingsService settings = await CreateSettingsAsync();
        settings.Settings.CloudBackup.CloudBackupIntervalMinutes = 10;
        await using var service = CreateService(api, auth, settings, () => now);

        Assert.Equal(AccountBackupEnableResult.Enabled, await service.SetEnabledAsync(true));
        Assert.Equal(1, api.CreateCalls);
        Assert.False(await service.RunIfDueAsync(force: false));

        now = now.AddMinutes(10);
        Assert.True(await service.RunIfDueAsync(force: false));
        Assert.Equal(2, api.CreateCalls);

        auth.SetUser(null);
        now = now.AddDays(1);
        Assert.False(await service.RunIfDueAsync(force: true));
        Assert.Equal(2, api.CreateCalls);
    }

    [Fact]
    public async Task OfflineUploadRecordsFailureAndKeepsAccountBindingForRetry()
    {
        DateTimeOffset now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
        var auth = new FakeAuth(new AuthUser("user-1", "Simon"));
        var api = new FakeApi { Failure = new HttpRequestException("offline") };
        SettingsService settings = await CreateSettingsAsync();
        await using var service = CreateService(api, auth, settings, () => now);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.SetEnabledAsync(true));

        Assert.True(settings.Settings.CloudBackup.AccountCloudBackupEnabled);
        Assert.Equal("user-1", settings.Settings.CloudBackup.AccountCloudBackupOwnerUserId);
        Assert.Equal(now.UtcTicks, settings.Settings.CloudBackup.AccountCloudBackupLastFailureUtcTicks);
        Assert.Equal(0, settings.Settings.CloudBackup.AccountCloudBackupLastSuccessUtcTicks);
        Assert.False(service.IsBusy);
    }

    private async Task<SettingsService> CreateSettingsAsync()
    {
        string data = Path.Combine(_root, "data");
        var settings = new SettingsService(data);
        await settings.LoadAsync();
        await settings.SaveAsync();
        return settings;
    }

    private AccountCloudBackupService CreateService(
        FakeApi api,
        FakeAuth auth,
        SettingsService settings,
        Func<DateTimeOffset> clock) => new(
            api,
            auth,
            settings,
            new DeskBoxDataBackupService(Path.Combine(_root, "backup-source")),
            Path.Combine(_root, "data"),
            clock);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeAuth(AuthUser? user) : IAuthSession
    {
        public event Action? SessionChanged;
        public bool IsAuthenticated => CurrentUser is not null;
        public AuthUser? CurrentUser { get; private set; } = user;
        internal void SetUser(AuthUser? value) { CurrentUser = value; SessionChanged?.Invoke(); }
        public Task<AuthUser> RegisterAsync(string username, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthUser> SignInAsync(string username, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeApi : IAuthenticatedAccountApi
    {
        internal Exception? Failure { get; set; }
        internal int CreateCalls { get; private set; }

        public Task<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string relativePath,
            byte[]? jsonBody,
            string? contentEncoding,
            CancellationToken cancellationToken = default)
        {
            if (relativePath.Contains("backup/create.php", StringComparison.Ordinal)) CreateCalls++;
            if (Failure is not null) throw Failure;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
