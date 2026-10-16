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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DingTalkGroupDocument[] _documents = [];
    private string? _loadedAccountKey;
    private string? _statePath;

    public DingTalkGroupDocumentService(IDingTalkDwsClient dws, string? stateDirectory = null)
    {
        _dws = dws;
        _stateDirectory = stateDirectory ?? Path.Combine(
            DeskBoxDataPathService.Current.DataDirectory, "sync", "dingtalk");
    }

    public IReadOnlyList<DingTalkGroupDocument> Documents => _documents;
    public DateTimeOffset? LastRefresh { get; private set; }

    public async Task<IReadOnlyList<DingTalkGroupDocument>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedStatePathAsync(cancellationToken);
        if (_loadedAccountKey is not null && _documents.Length != 0) return _documents;
        if (_statePath is null || !File.Exists(_statePath)) return _documents;
        try
        {
            string json = await File.ReadAllTextAsync(_statePath, cancellationToken);
            DingTalkGroupDocumentStore? store = JsonSerializer.Deserialize(
                json, DingTalkGroupDocumentJsonContext.Default.DingTalkGroupDocumentStore);
            _documents = store?.Documents ?? [];
            LastRefresh = store?.LastRefresh;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            App.Log($"[DingTalkGroupDocs] Local index load failed: {ex.GetType().Name}");
        }
        return _documents;
    }

    public async Task<IReadOnlyList<DingTalkGroupDocument>> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureAuthenticatedStatePathAsync(cancellationToken);

            var byGroupAndUrl = new Dictionary<string, DingTalkGroupDocument>(StringComparer.OrdinalIgnoreCase);
            DateTimeOffset end = DateTimeOffset.Now;
            DateTimeOffset cursorStart = end.AddYears(-10);
            while (cursorStart < end)
            {
                DateTimeOffset cursorEnd = cursorStart.AddDays(90);
                if (cursorEnd > end) cursorEnd = end;
                string? cursor = null;
                int pages = 0;
                do
                {
                    DingTalkGroupMessagePage page = await _dws.ListGroupMessagesAsync(
                        cursorStart, cursorEnd, cursor, 50, cancellationToken);
                    foreach (DingTalkGroupMessage message in page.Messages)
                    {
                        if (message.IsSingleChat || string.IsNullOrWhiteSpace(message.Content)) continue;
                        AddDocuments(byGroupAndUrl, message);
                    }

                    cursor = page.HasMore ? page.NextCursor : null;
                    pages++;
                }
                while (cursor is not null && pages < 200);

                cursorStart = cursorEnd;
                if (byGroupAndUrl.Count >= 50000) break;
            }

            _documents = byGroupAndUrl.Values
                .OrderByDescending(document => document.LastSeenAt)
                .ThenBy(document => document.GroupName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(document => document.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            LastRefresh = DateTimeOffset.Now;
            await SaveAsync(cancellationToken);
            return _documents;
        }
        finally { _gate.Release(); }
    }

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
        string key = $"{groupName}\u001f{url}";
        if (!byGroupAndUrl.TryGetValue(key, out DingTalkGroupDocument? existing) ||
            existing.LastSeenAt < document.LastSeenAt)
        {
            byGroupAndUrl[key] = document;
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_statePath is null)
            throw new InvalidOperationException("DWS account state is not initialized.");
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var store = new DingTalkGroupDocumentStore
        {
            LastRefresh = LastRefresh,
            Documents = _documents
        };
        string json = JsonSerializer.Serialize(
            store, DingTalkGroupDocumentJsonContext.Default.DingTalkGroupDocumentStore);
        cancellationToken.ThrowIfCancellationRequested();
        await ResilientJsonStore.SaveAsync(_statePath, json);
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
    }
}

internal sealed class DingTalkGroupDocumentStore
{
    public DateTimeOffset? LastRefresh { get; set; }
    public DingTalkGroupDocument[] Documents { get; set; } = [];
}

[System.Text.Json.Serialization.JsonSerializable(typeof(DingTalkGroupDocumentStore))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DingTalkGroupDocument[]))]
internal sealed partial class DingTalkGroupDocumentJsonContext :
    System.Text.Json.Serialization.JsonSerializerContext
{
}
