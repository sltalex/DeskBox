using System.Net;
using System.Text;
using System.Text.Json;
using DeskBox.Contracts;
using DeskBox.Core.Persistence;

namespace DeskBox.Services;

internal sealed class TreeCloudSyncService : IAsyncDisposable
{
    private readonly IAuthenticatedAccountApi _api;
    private readonly IAuthSession _auth;
    private readonly Func<TreeProgress> _readProgress;
    private readonly string _stateDirectory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private PeriodicTimer? _timer;
    private Task? _timerTask;
    private int _disposed;

    internal TreeCloudSyncService(
        IAuthenticatedAccountApi api,
        IAuthSession auth,
        Func<TreeProgress>? readProgress = null,
        string? stateDirectory = null,
        Func<DateTimeOffset>? clock = null)
    {
        _api = api;
        _auth = auth;
        _readProgress = readProgress ?? (() => TreeCounterService.Instance.Snapshot);
        _stateDirectory = stateDirectory ?? Path.Combine(DeskBoxDataPathService.Current.DataDirectory, "sync", "tree");
        _clock = clock ?? (() => DateTimeOffset.Now);
        _auth.SessionChanged += OnSessionChanged;
    }

    internal event Action? LeaderboardChanged;
    internal TreeLeaderboardEntryDto[] Leaderboard { get; private set; } = [];
    internal TreeLeaderboardCurrentUserDto CurrentUserRank { get; private set; } = new();
    internal DateTimeOffset? LastLeaderboardSuccess { get; private set; }
    internal bool IsOffline { get; private set; }

    internal void Start()
    {
        if (_timerTask is not null) return;
        _timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        _timerTask = RunTimerAsync(_lifetime.Token);
        if (_auth.IsAuthenticated) _ = RunCycleSafelyAsync(_lifetime.Token);
    }

    internal async Task<bool> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (_auth.CurrentUser is not { } user) return false;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_stateDirectory);
            string statePath = StatePath(user.Id);
            TreeCloudState state = await LoadStateAsync(statePath, user.Id, cancellationToken);
            TreeProgress local = _readProgress();
            string localDay = string.IsNullOrWhiteSpace(local.LocalDay)
                ? _clock().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                : local.LocalDay;
            long fruitTotal = checked(local.FruitInventory + local.HarvestedFruit);

            string rebaselinePath = RebaselinePath(user.Id);
            if (File.Exists(rebaselinePath))
            {
                state.EnergyBaseline = local.TotalEnergy;
                state.FruitBaseline = fruitTotal;
                state.HarvestedBaseline = local.HarvestedFruit;
                state.Day = localDay;
                state.DayBaseline = local.TodayKeys;
                state.Pending = null;
                await SaveStateAsync(statePath, state, cancellationToken);
                AccountBackupFileCatalog.DeleteFileIfExists(rebaselinePath);
                return false;
            }

            if (state.Pending is null)
            {
                bool rebaselined = false;
                if (local.TotalEnergy < state.EnergyBaseline) { state.EnergyBaseline = local.TotalEnergy; rebaselined = true; }
                if (fruitTotal < state.FruitBaseline) { state.FruitBaseline = fruitTotal; rebaselined = true; }
                if (local.HarvestedFruit < state.HarvestedBaseline) { state.HarvestedBaseline = local.HarvestedFruit; rebaselined = true; }
                if (!string.Equals(state.Day, localDay, StringComparison.Ordinal))
                {
                    state.Day = localDay;
                    state.DayBaseline = 0;
                    rebaselined = true;
                }
                else if (local.TodayKeys < state.DayBaseline) { state.DayBaseline = local.TodayKeys; rebaselined = true; }

                long energyDelta = local.TotalEnergy - state.EnergyBaseline;
                long fruitDelta = fruitTotal - state.FruitBaseline;
                long harvestedDelta = local.HarvestedFruit - state.HarvestedBaseline;
                long dayDelta = local.TodayKeys - state.DayBaseline;
                if (energyDelta == 0 && fruitDelta == 0 && harvestedDelta == 0 && dayDelta == 0)
                {
                    if (rebaselined) await SaveStateAsync(statePath, state, cancellationToken);
                    return false;
                }

                state.Pending = new TreePendingOperation
                {
                    Request = new TreeProgressUploadRequest
                    {
                        OperationId = Guid.NewGuid().ToString("N"), DeviceId = DeviceIdentity.Id, Day = localDay,
                        EnergyDelta = energyDelta, DayGrowthDelta = dayDelta,
                        FruitDelta = fruitDelta, HarvestedDelta = harvestedDelta
                    },
                    CapturedEnergy = local.TotalEnergy,
                    CapturedFruit = fruitTotal,
                    CapturedHarvested = local.HarvestedFruit,
                    CapturedDayGrowth = local.TodayKeys
                };
                await SaveStateAsync(statePath, state, cancellationToken);
            }

            byte[] json = JsonSerializer.SerializeToUtf8Bytes(
                state.Pending.Request, AccountCloudJsonContext.Default.TreeProgressUploadRequest);
            using HttpResponseMessage response = await _api.SendAsync(
                HttpMethod.Post, "../tree/progress.php", json, null, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            state.EnergyBaseline = state.Pending.CapturedEnergy;
            state.FruitBaseline = state.Pending.CapturedFruit;
            state.HarvestedBaseline = state.Pending.CapturedHarvested;
            state.Day = state.Pending.Request.Day;
            state.DayBaseline = state.Pending.CapturedDayGrowth;
            state.Pending = null;
            await SaveStateAsync(statePath, state, cancellationToken);
            IsOffline = false;
            _ = RefreshLeaderboardAfterUploadAsync();
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            IsOffline = true;
            RaiseLeaderboardChanged();
            throw;
        }
        finally { _gate.Release(); }
    }

    internal async Task<TreeLeaderboardEntryDto[]> RefreshLeaderboardAsync(CancellationToken cancellationToken = default)
    {
        if (!_auth.IsAuthenticated) return Leaderboard;
        string day = _clock().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            using HttpResponseMessage response = await _api.SendAsync(
                HttpMethod.Get, $"../tree/leaderboard.php?day={day}", null, null, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            TreeLeaderboardResponse body = await JsonSerializer.DeserializeAsync(
                stream, AccountCloudJsonContext.Default.TreeLeaderboardResponse, cancellationToken)
                ?? throw new InvalidDataException("The tree leaderboard response is empty.");
            Leaderboard = body.Entries;
            CurrentUserRank = body.CurrentUser;
            LastLeaderboardSuccess = DateTimeOffset.UtcNow;
            IsOffline = false;
            RaiseLeaderboardChanged();
            return Leaderboard;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            IsOffline = true;
            RaiseLeaderboardChanged();
            return Leaderboard;
        }
    }

    internal async Task SetParticipationAsync(bool participating, CancellationToken cancellationToken = default)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            new TreeProfileUpdateRequest { Participating = participating },
            AccountCloudJsonContext.Default.TreeProfileUpdateRequest);
        using HttpResponseMessage response = await _api.SendAsync(
            HttpMethod.Post, "../tree/profile.php", json, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        CurrentUserRank.Participating = participating;
        await RefreshLeaderboardAsync(cancellationToken);
    }

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_timer is not null && await _timer.WaitForNextTickAsync(cancellationToken))
                await RunCycleSafelyAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task RunCycleSafelyAsync(CancellationToken cancellationToken)
    {
        try { await SyncNowAsync(cancellationToken); } catch (Exception ex) when (ex is not OperationCanceledException) { App.Log($"[TreeCloud] Progress sync failed: {ex.GetType().Name}"); }
        try { await RefreshLeaderboardAsync(cancellationToken); } catch (Exception ex) when (ex is not OperationCanceledException) { App.Log($"[TreeCloud] Leaderboard refresh failed: {ex.GetType().Name}"); }
    }

    private void OnSessionChanged()
    {
        RaiseLeaderboardChanged();
        if (_auth.IsAuthenticated) _ = RunCycleSafelyAsync(_lifetime.Token);
    }

    private async Task RefreshLeaderboardAfterUploadAsync()
    {
        try { await RefreshLeaderboardAsync(_lifetime.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { App.Log($"[TreeCloud] Post-upload ranking refresh failed: {ex.GetType().Name}"); }
    }

    private async Task<TreeCloudState> LoadStateAsync(string path, string userId, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new TreeCloudState { UserId = userId };
        try
        {
            TreeCloudState? state = JsonSerializer.Deserialize(
                await File.ReadAllTextAsync(path, cancellationToken), AccountCloudJsonContext.Default.TreeCloudState);
            return state is not null && string.Equals(state.UserId, userId, StringComparison.Ordinal)
                ? state : new TreeCloudState { UserId = userId };
        }
        catch (JsonException) { return new TreeCloudState { UserId = userId }; }
    }

    private static Task SaveStateAsync(string path, TreeCloudState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ResilientJsonStore.SaveAsync(
            path,
            JsonSerializer.Serialize(state, AccountCloudJsonContext.Default.TreeCloudState));
    }

    private string StatePath(string userId) => Path.Combine(_stateDirectory, $"state-{AccountBackupFileCatalog.SafeAccountId(userId)}.json");
    private string RebaselinePath(string userId) => Path.Combine(_stateDirectory, $"rebaseline-{AccountBackupFileCatalog.SafeAccountId(userId)}.json");

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
        throw new AuthApiException(error?.Code ?? "server_error", error?.Message ?? "Tree cloud request failed.", response.StatusCode);
    }

    private void RaiseLeaderboardChanged()
    {
        foreach (Action observer in LeaderboardChanged?.GetInvocationList() ?? [])
        {
            try { observer(); } catch (Exception ex) { App.Log($"[TreeCloud] Observer failed: {ex.GetType().Name}"); }
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
        _gate.Dispose();
    }
}
