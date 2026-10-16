using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeskBox.Core.Persistence;

namespace DeskBox.Services;

/// <summary>Local, DWS-account-scoped stars for indexed group-document links.</summary>
public sealed class DingTalkGroupDocumentFavorites
{
    private readonly IDingTalkDwsClient _dws;
    private readonly string _stateDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile HashSet<string> _urls = new(StringComparer.Ordinal);
    private string? _loadedAccountKey;
    private string? _statePath;

    public DingTalkGroupDocumentFavorites(IDingTalkDwsClient dws, string? stateDirectory = null)
    {
        _dws = dws;
        _stateDirectory = stateDirectory ?? Path.Combine(
            DeskBoxDataPathService.Current.DataDirectory, "sync", "dingtalk");
    }

    public event Action? Changed;

    public bool IsFavorite(string url) => _urls.Contains(DocumentKey(url));

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { await LoadCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task SetFavoriteAsync(string url, bool favorite, CancellationToken cancellationToken = default)
    {
        string key = DocumentKey(url);
        bool changed = false;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await LoadCoreAsync(cancellationToken);
            var next = new HashSet<string>(_urls, StringComparer.Ordinal);
            changed = favorite ? next.Add(key) : next.Remove(key);
            if (!changed) return;
            Directory.CreateDirectory(_stateDirectory);
            var store = new DingTalkGroupDocumentFavoritesStore
            {
                DocumentUrls = next.Order(StringComparer.Ordinal).ToArray()
            };
            string json = JsonSerializer.Serialize(
                store, DingTalkGroupDocumentFavoritesJsonContext.Default.DingTalkGroupDocumentFavoritesStore);
            cancellationToken.ThrowIfCancellationRequested();
            await ResilientJsonStore.SaveAsync(_statePath!, json);
            _urls = next;
        }
        finally { _gate.Release(); }
        if (changed) Changed?.Invoke();
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        DingTalkDwsAuthStatus status = await _dws.GetAuthStatusAsync(cancellationToken);
        if (!status.Authenticated || !status.TokenValid ||
            string.IsNullOrWhiteSpace(status.CorpId) || string.IsNullOrWhiteSpace(status.UserId))
            throw new DingTalkDwsAuthorizationRequiredException("DWS authorization is required.");

        string accountKey = $"{status.CorpId.Trim()}\u001f{status.UserId.Trim()}";
        if (string.Equals(_loadedAccountKey, accountKey, StringComparison.Ordinal)) return;
        // Never expose the previous account's stars if the next account's
        // local file cannot be read or parsed.
        _urls = new HashSet<string>(StringComparer.Ordinal);
        _statePath = null;
        _loadedAccountKey = null;
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(accountKey));
        string path = Path.Combine(_stateDirectory,
            $"group-document-favorites-{Convert.ToHexString(digest).ToLowerInvariant()}.json");
        var urls = new HashSet<string>(StringComparer.Ordinal);
        if (File.Exists(path))
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            DingTalkGroupDocumentFavoritesStore? store = JsonSerializer.Deserialize(
                json, DingTalkGroupDocumentFavoritesJsonContext.Default.DingTalkGroupDocumentFavoritesStore);
            foreach (string url in store?.DocumentUrls ?? [])
            {
                if (TryDocumentKey(url, out string? key) && key is not null) urls.Add(key);
            }
        }
        _urls = urls;
        _statePath = path;
        _loadedAccountKey = accountKey;
    }

    private static string DocumentKey(string url)
    {
        if (TryDocumentKey(url, out string? key) && key is not null) return key;
        throw new ArgumentException("A DingTalk document URL is required.", nameof(url));
    }

    private static bool TryDocumentKey(string? url, out string? key)
    {
        key = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "alidocs.dingtalk.com", StringComparison.OrdinalIgnoreCase))
            return false;
        key = uri.GetLeftPart(UriPartial.Path);
        return true;
    }
}

internal sealed class DingTalkGroupDocumentFavoritesStore
{
    public string[] DocumentUrls { get; set; } = [];
}

[JsonSerializable(typeof(DingTalkGroupDocumentFavoritesStore))]
internal sealed partial class DingTalkGroupDocumentFavoritesJsonContext : JsonSerializerContext
{
}
