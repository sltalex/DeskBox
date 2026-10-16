using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using DeskBox.Core.Persistence;

namespace DeskBox.Services;

public sealed record DingTalkGroupDocument(
    string GroupName,
    string Title,
    string Url,
    DateTimeOffset LastSeenAt);

public sealed record DingTalkGroupDocumentSyncProgress(
    int CompletedSegments,
    int TotalSegments,
    bool IsInitialSync,
    bool IsRunning);

public sealed class DingTalkGroupDocumentService
{
    private static readonly Regex MarkdownLinkRegex = new(
        @"\[(?<title>[^\]]+)\]\((?<url>https://alidocs\.dingtalk\.com/[^\s\)<>]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex PlainLinkRegex = new(
        @"(?<url>https://alidocs\.dingtalk\.com/[^\s\]\)<>]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly IDingTalkDwsClient _dws;
    private readonly string _stateDirectory;
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DingTalkGroupDocument[] _documents = [];
    private string? _loadedAccountKey;
    private string? _statePath;
    private bool _stateLoaded;

    public DingTalkGroupDocumentService(IDingTalkDwsClient dws, string? stateDirectory = null)
    {
        _dws = dws;
        _stateDirectory = stateDirectory ?? Path.Combine(
            DeskBoxDataPathService.Current.DataDirectory, "sync", "dingtalk");
        _settingsPath = Path.Combine(_stateDirectory, "group-documents-settings.json");
        LoadIntervalSettingsFromDisk();
    }

    public IReadOnlyList<DingTalkGroupDocument> Documents => _documents;
    public DateTimeOffset? LastRefresh { get; private set; }
    public DateTimeOffset? LastSyncStartedAt { get; private set; }
    public DateTimeOffset? LastSyncCompletedAt { get; private set; }
    public bool HasCompletedInitialSync { get; private set; }
    public int SyncIntervalHours { get; private set; } = 6;
    public bool IsSyncing { get; private set; }

    public event Action<DingTalkGroupDocumentSyncProgress>? ProgressChanged;

    public async Task<IReadOnlyList<DingTalkGroupDocument>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await LoadCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<DingTalkGroupDocument>> LoadCoreAsync(
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedStatePathAsync(cancellationToken);
        if (_stateLoaded) return _documents;
        if (_statePath is null || !File.Exists(_statePath))
        {
            await LoadIntervalSettingsAsync(cancellationToken);
            _stateLoaded = true;
            return _documents;
        }
        try
        {
            string json = await File.ReadAllTextAsync(_statePath, cancellationToken);
            DingTalkGroupDocumentStore? store = JsonSerializer.Deserialize(
                json, DingTalkGroupDocumentJsonContext.Default.DingTalkGroupDocumentStore);
            _documents = store?.Documents ?? [];
            LastRefresh = store?.LastRefresh;
            LastSyncStartedAt = store?.LastSyncStartedAt;
            LastSyncCompletedAt = store?.LastSyncCompletedAt ?? store?.LastRefresh;
            HasCompletedInitialSync = store?.HasCompletedInitialSync == true || store?.LastRefresh is not null;
            SyncIntervalHours = NormalizeInterval(store?.SyncIntervalHours ?? 6);
            await LoadIntervalSettingsAsync(cancellationToken);
            _stateLoaded = true;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            App.Log($"[DingTalkGroupDocs] Local index load failed: {ex.GetType().Name}");
            _stateLoaded = true;
        }
        return _documents;
    }

    public async Task<IReadOnlyList<DingTalkGroupDocument>> RefreshAsync(
        CancellationToken cancellationToken = default)
        => await SyncAsync(cancellationToken);

    public async Task<IReadOnlyList<DingTalkGroupDocument>> SyncAsync(
        CancellationToken cancellationToken = default)
        => await SyncAsync(canCommit: null, cancellationToken);

    internal async Task<IReadOnlyList<DingTalkGroupDocument>> SyncAsync(
        Func<bool>? canCommit, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureAuthenticatedStatePathAsync(cancellationToken);
            await LoadCoreAsync(cancellationToken);
            string syncAccountKey = _loadedAccountKey!;

            bool initialSync = !HasCompletedInitialSync;
            DateTimeOffset end = DateTimeOffset.Now;
            DateTimeOffset cursorStart = initialSync
                ? new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero)
                : (LastSyncCompletedAt ?? end.AddDays(-2)).AddDays(-2);
            if (cursorStart >= end) cursorStart = end.AddHours(-1);

            var segments = new List<(DateTimeOffset Start, DateTimeOffset End)>();
            while (cursorStart < end)
            {
                DateTimeOffset cursorEnd = cursorStart.AddDays(90);
                if (cursorEnd > end) cursorEnd = end;
                segments.Add((cursorStart, cursorEnd));
                cursorStart = cursorEnd;
            }

            var byGroupAndUrl = _documents.ToDictionary(
                document => MakeKey(document.GroupName, document.Url),
                document => document,
                StringComparer.OrdinalIgnoreCase);
            IsSyncing = true;
            LastSyncStartedAt = DateTimeOffset.Now;
            ProgressChanged?.Invoke(new(0, segments.Count, initialSync, true));
            int completed = 0;
            foreach ((DateTimeOffset segmentStart, DateTimeOffset segmentEnd) in segments)
            {
                string? cursor = null;
                int pages = 0;
                do
                {
                    DingTalkGroupMessagePage page = await _dws.ListGroupMessagesAsync(
                        segmentStart, segmentEnd, cursor, 50, cancellationToken);
                    foreach (DingTalkGroupMessage message in page.Messages)
                    {
                        if (message.IsSingleChat || string.IsNullOrWhiteSpace(message.Content)) continue;
                        AddDocuments(byGroupAndUrl, message);
                    }

                    if (page.HasMore && string.IsNullOrWhiteSpace(page.NextCursor))
                        throw new InvalidDataException("DWS returned an incomplete group-message page.");
                    cursor = page.HasMore ? page.NextCursor : null;
                    pages++;
                }
                while (cursor is not null && pages < 200);

                if (cursor is not null)
                    throw new InvalidDataException("DWS group-message pagination exceeded the safety limit.");

                completed++;
                ProgressChanged?.Invoke(new(completed, segments.Count, initialSync, true));
                if (byGroupAndUrl.Count >= 50000)
                    throw new InvalidDataException("The group-document index exceeded the safety limit.");
            }

            DingTalkDwsAuthStatus finalStatus = await _dws.GetAuthStatusAsync(cancellationToken);
            string finalAccountKey = $"{finalStatus.CorpId.Trim()}\u001f{finalStatus.UserId.Trim()}";
            if (!finalStatus.Authenticated || !finalStatus.TokenValid ||
                !string.Equals(syncAccountKey, finalAccountKey, StringComparison.Ordinal))
                throw new DingTalkDwsAuthorizationRequiredException("The DWS identity changed during synchronization.");
            if (canCommit is not null && !canCommit())
                throw new OperationCanceledException("The linked account changed during synchronization.", cancellationToken);

            DingTalkGroupDocument[] completedDocuments = byGroupAndUrl.Values
                .OrderByDescending(document => document.LastSeenAt)
                .ThenBy(document => document.GroupName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(document => document.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            DateTimeOffset completedAt = DateTimeOffset.Now;
            cancellationToken.ThrowIfCancellationRequested();
            await SaveAsync(new DingTalkGroupDocumentStore
            {
                LastRefresh = completedAt,
                LastSyncStartedAt = LastSyncStartedAt,
                LastSyncCompletedAt = completedAt,
                HasCompletedInitialSync = true,
                SyncIntervalHours = SyncIntervalHours,
                Documents = completedDocuments
            }, cancellationToken);
            _documents = completedDocuments;
            LastRefresh = completedAt;
            LastSyncCompletedAt = completedAt;
            HasCompletedInitialSync = true;
            return _documents;
        }
        finally
        {
            IsSyncing = false;
            try { ProgressChanged?.Invoke(new(0, 0, false, false)); }
            finally { _gate.Release(); }
        }
    }

    public async Task SetSyncIntervalHoursAsync(int hours, CancellationToken cancellationToken = default)
    {
        int interval = NormalizeInterval(hours);
        Directory.CreateDirectory(_stateDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        await ResilientJsonStore.SaveAsync(
            _settingsPath,
            $"{{\"syncIntervalHours\":{interval.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}");
        SyncIntervalHours = interval;
    }

    private static int NormalizeInterval(int hours) => Math.Clamp(hours, 1, 168);

    private static void AddDocuments(
        Dictionary<string, DingTalkGroupDocument> byGroupAndUrl,
        DingTalkGroupMessage message)
    {
        foreach (Match match in MarkdownLinkRegex.Matches(message.Content))
        {
            AddDocument(byGroupAndUrl, message, match.Groups["title"].Value, match.Groups["url"].Value);
        }

        foreach (Match match in PlainLinkRegex.Matches(message.Content))
        {
            AddDocument(byGroupAndUrl, message, string.Empty, match.Groups["url"].Value);
        }
    }

    private static void AddDocument(
        Dictionary<string, DingTalkGroupDocument> byGroupAndUrl,
        DingTalkGroupMessage message,
        string title,
        string url)
    {
        url = url.TrimEnd('.', ',', ';', '。', '，', '；');
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) return;
        title = string.IsNullOrWhiteSpace(title) || title.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? "群文档"
            : title.Trim();
        string groupName = string.IsNullOrWhiteSpace(message.GroupName) ? "未命名群" : message.GroupName.Trim();
        var document = new DingTalkGroupDocument(
            groupName,
            title,
            url,
            message.CreatedAt);
        string key = MakeKey(groupName, url);
        if (!byGroupAndUrl.TryGetValue(key, out DingTalkGroupDocument? existing) ||
            existing.LastSeenAt < document.LastSeenAt)
        {
            byGroupAndUrl[key] = document;
        }
    }

    private static string MakeKey(string groupName, string url) => $"{groupName}\u001f{url}";

    private async Task SaveAsync(DingTalkGroupDocumentStore store, CancellationToken cancellationToken)
    {
        if (_statePath is null)
            throw new InvalidOperationException("DWS account state is not initialized.");
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        string json = JsonSerializer.Serialize(
            store, DingTalkGroupDocumentJsonContext.Default.DingTalkGroupDocumentStore);
        cancellationToken.ThrowIfCancellationRequested();
        await ResilientJsonStore.SaveAsync(_statePath, json);
    }

    private async Task LoadIntervalSettingsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath)) return;
        try
        {
            string value = await File.ReadAllTextAsync(_settingsPath, cancellationToken);
            ApplyIntervalSettings(value);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            App.Log($"[DingTalkGroupDocs] Sync settings load failed: {ex.GetType().Name}");
        }
    }

    private void LoadIntervalSettingsFromDisk()
    {
        if (!File.Exists(_settingsPath)) return;
        try { ApplyIntervalSettings(File.ReadAllText(_settingsPath)); }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            App.Log($"[DingTalkGroupDocs] Sync settings load failed: {ex.GetType().Name}");
        }
    }

    private void ApplyIntervalSettings(string value)
    {
        using JsonDocument document = JsonDocument.Parse(value);
        if (document.RootElement.TryGetProperty("syncIntervalHours", out JsonElement hours) &&
            hours.TryGetInt32(out int interval))
            SyncIntervalHours = NormalizeInterval(interval);
    }

    private async Task EnsureAuthenticatedStatePathAsync(CancellationToken cancellationToken)
    {
        DingTalkDwsAuthStatus status = await _dws.GetAuthStatusAsync(cancellationToken);
        if (!status.Authenticated || !status.TokenValid ||
            string.IsNullOrWhiteSpace(status.CorpId) || string.IsNullOrWhiteSpace(status.UserId))
        {
            throw new DingTalkDwsAuthorizationRequiredException("DWS authorization is required.");
        }

        string accountKey = $"{status.CorpId.Trim()}\u001f{status.UserId.Trim()}";
        if (string.Equals(_loadedAccountKey, accountKey, StringComparison.Ordinal)) return;

        byte[] digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(accountKey));
        string fileName = $"group-documents-{Convert.ToHexString(digest).ToLowerInvariant()}.json";
        _statePath = Path.Combine(_stateDirectory, fileName);
        _loadedAccountKey = accountKey;
        _documents = [];
        LastRefresh = null;
        LastSyncStartedAt = null;
        LastSyncCompletedAt = null;
        HasCompletedInitialSync = false;
        SyncIntervalHours = 6;
        _stateLoaded = false;
    }
}

internal sealed class DingTalkGroupDocumentStore
{
    public DateTimeOffset? LastRefresh { get; set; }
    public DateTimeOffset? LastSyncStartedAt { get; set; }
    public DateTimeOffset? LastSyncCompletedAt { get; set; }
    public bool HasCompletedInitialSync { get; set; }
    public int SyncIntervalHours { get; set; } = 6;
    public DingTalkGroupDocument[] Documents { get; set; } = [];
}

[System.Text.Json.Serialization.JsonSerializable(typeof(DingTalkGroupDocumentStore))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DingTalkGroupDocument[]))]
internal sealed partial class DingTalkGroupDocumentJsonContext :
    System.Text.Json.Serialization.JsonSerializerContext
{
}
