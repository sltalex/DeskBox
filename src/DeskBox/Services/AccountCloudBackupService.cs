using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using DeskBox.Contracts;
using DeskBox.Core.Persistence;

namespace DeskBox.Services;

internal enum AccountBackupEnableResult
{
    Enabled,
    Disabled,
    SignInRequired
}

internal sealed class AccountCloudBackupService : IAsyncDisposable
{
    private readonly IAuthenticatedAccountApi _api;
    private readonly IAuthSession _auth;
    private readonly SettingsService _settings;
    private readonly DeskBoxDataBackupService _localBackup;
    private readonly AccountBackupFileCatalog _catalog;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private PeriodicTimer? _timer;
    private Task? _timerTask;
    private int _disposed;

    internal AccountCloudBackupService(
        IAuthenticatedAccountApi api,
        IAuthSession auth,
        SettingsService settings,
        DeskBoxDataBackupService localBackup,
        string? dataDirectory = null,
        Func<DateTimeOffset>? clock = null)
    {
        _api = api;
        _auth = auth;
        _settings = settings;
        _localBackup = localBackup;
        _catalog = new AccountBackupFileCatalog(dataDirectory ?? DeskBoxDataPathService.Current.DataDirectory);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _auth.SessionChanged += OnSessionChanged;
    }

    internal event Action? StateChanged;
    internal AccountBackupSnapshotDto[] Snapshots { get; private set; } = [];
    internal bool IsBusy { get; private set; }

    internal void Start()
    {
        if (_timerTask is not null) return;
        _timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        _timerTask = RunTimerAsync(_lifetime.Token);
        _ = RunIfDueAsync(force: false, _lifetime.Token);
    }

    internal async Task<AccountBackupEnableResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (enabled && (!_auth.IsAuthenticated || _auth.CurrentUser is null))
            return AccountBackupEnableResult.SignInRequired;

        var cloud = _settings.Settings.CloudBackup;
        cloud.AccountCloudBackupEnabled = enabled;
        if (enabled)
        {
            cloud.AccountCloudBackupOwnerUserId = _auth.CurrentUser!.Id;
            cloud.AccountCloudBackupLastSuccessUtcTicks = 0;
            cloud.AccountCloudBackupLastFailureUtcTicks = 0;
        }
        cancellationToken.ThrowIfCancellationRequested();
        await _settings.SaveAsync();
        RaiseStateChanged();
        if (enabled) await RunIfDueAsync(force: true, cancellationToken);
        return enabled ? AccountBackupEnableResult.Enabled : AccountBackupEnableResult.Disabled;
    }

    internal bool IsEnabledForCurrentAccount
    {
        get
        {
            var cloud = _settings.Settings.CloudBackup;
            return cloud.AccountCloudBackupEnabled && _auth.CurrentUser is { } user &&
                   string.Equals(cloud.AccountCloudBackupOwnerUserId, user.Id, StringComparison.Ordinal);
        }
    }

    internal bool IsBoundToCurrentAccount => _auth.CurrentUser is { } user &&
        string.Equals(_settings.Settings.CloudBackup.AccountCloudBackupOwnerUserId, user.Id, StringComparison.Ordinal);

    internal async Task<bool> RunIfDueAsync(bool force, CancellationToken cancellationToken = default)
    {
        if (!IsEnabledForCurrentAccount) return false;
        var cloud = _settings.Settings.CloudBackup;
        DateTimeOffset lastSuccess = cloud.AccountCloudBackupLastSuccessUtcTicks > 0
            ? new DateTimeOffset(cloud.AccountCloudBackupLastSuccessUtcTicks, TimeSpan.Zero)
            : DateTimeOffset.MinValue;
        if (!force && _clock() - lastSuccess < TimeSpan.FromMinutes(Math.Clamp(cloud.CloudBackupIntervalMinutes, 5, 30 * 24 * 60)))
            return false;
        if (!await _operation.WaitAsync(0, cancellationToken)) return false;
        SetBusy(true);
        try
        {
            AccountBackupFileDto[] files = await _catalog.CaptureAsync(cancellationToken);
            var request = new AccountBackupCreateRequest
            {
                DeviceId = DeviceIdentity.Id,
                AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
                Files = files
            };
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(request, AccountCloudJsonContext.Default.AccountBackupCreateRequest);
            byte[] compressed;
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
                    await gzip.WriteAsync(json, cancellationToken);
                compressed = output.ToArray();
            }
            using HttpResponseMessage response = await _api.SendAsync(
                HttpMethod.Post, "../backup/create.php", compressed, "gzip", cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            cloud.AccountCloudBackupLastSuccessUtcTicks = _clock().UtcTicks;
            cloud.AccountCloudBackupLastFailureUtcTicks = 0;
            cancellationToken.ThrowIfCancellationRequested();
            await _settings.SaveAsync();
            return true;
        }
        catch
        {
            cloud.AccountCloudBackupLastFailureUtcTicks = _clock().UtcTicks;
            try { await _settings.SaveAsync(); } catch { }
            throw;
        }
        finally
        {
            _operation.Release();
            SetBusy(false);
        }
    }

    internal async Task<AccountBackupSnapshotDto[]> RefreshSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        if (!await _operation.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Another account backup operation is already running.");
        SetBusy(true);
        try
        {
            using HttpResponseMessage response = await _api.SendAsync(HttpMethod.Get, "../backup/list.php", null, null, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            AccountBackupListResponse body = await JsonSerializer.DeserializeAsync(
                stream, AccountCloudJsonContext.Default.AccountBackupListResponse, cancellationToken)
                ?? throw new InvalidDataException("The account backup service returned an empty list.");
            Snapshots = body.Snapshots;
            return Snapshots;
        }
        finally
        {
            _operation.Release();
            SetBusy(false);
        }
    }

    internal async Task PrepareRestoreAsync(string snapshotId, CancellationToken cancellationToken = default)
    {
        if (_auth.CurrentUser is not { } user) throw new AuthApiException("invalid_session", "Sign in is required.", System.Net.HttpStatusCode.Unauthorized);
        if (!await _operation.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Another account backup operation is already running.");
        SetBusy(true);
        try
        {
            string escaped = Uri.EscapeDataString(snapshotId);
            using HttpResponseMessage response = await _api.SendAsync(HttpMethod.Get, $"../backup/download.php?snapshot_id={escaped}", null, null, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            AccountBackupDownloadResponse body = await JsonSerializer.DeserializeAsync(
                stream, AccountCloudJsonContext.Default.AccountBackupDownloadResponse, cancellationToken)
                ?? throw new InvalidDataException("The account backup service returned an empty snapshot.");
            await _catalog.PrepareRestoreAsync(
                body.Snapshot,
                user.Id,
                async token => { _ = await _localBackup.ExportBackupAsync(_localBackup.PreRestoreBackupDirectory, token); },
                cancellationToken);
        }
        finally
        {
            _operation.Release();
            SetBusy(false);
        }
    }

    internal Task<AccountBackupRestoreResult> ApplyPendingRestoreAsync(CancellationToken cancellationToken = default) =>
        _catalog.ApplyPendingRestoreAsync(cancellationToken);

    internal void CancelPreparedRestore() => _catalog.CancelPreparedRestore();

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_timer is not null && await _timer.WaitForNextTickAsync(cancellationToken))
            {
                try { await RunIfDueAsync(force: false, cancellationToken); }
                catch (Exception ex) when (ex is not OperationCanceledException) { App.Log($"[AccountBackup] Scheduled upload failed: {ex.GetType().Name}"); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void OnSessionChanged()
    {
        RaiseStateChanged();
        if (IsEnabledForCurrentAccount) _ = RunIfDueAsync(force: false, _lifetime.Token);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        AuthErrorResponse? error = null;
        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            error = await JsonSerializer.DeserializeAsync(stream, AuthJsonContext.Default.AuthErrorResponse, cancellationToken);
        }
        catch (JsonException) { }
        throw new AuthApiException(error?.Code ?? "server_error", error?.Message ?? "Account backup request failed.", response.StatusCode);
    }

    private void SetBusy(bool value) { IsBusy = value; RaiseStateChanged(); }
    private void RaiseStateChanged()
    {
        foreach (Action observer in StateChanged?.GetInvocationList() ?? [])
        {
            try { observer(); } catch (Exception ex) { App.Log($"[AccountBackup] Observer failed: {ex.GetType().Name}"); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _auth.SessionChanged -= OnSessionChanged;
        _lifetime.Cancel();
        _timer?.Dispose();
        if (_timerTask is not null) { try { await _timerTask; } catch { } }
        _lifetime.Dispose();
        _operation.Dispose();
    }
}
