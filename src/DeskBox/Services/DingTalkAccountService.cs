using System.Net;
using System.Text.Json;

namespace DeskBox.Services;

public sealed class DingTalkAccountService
{
    private readonly IAuthenticatedAccountApi _api;

    /// <summary>
    /// The last identity loaded for the currently authenticated SltBox account.
    /// Feature services use this cache between explicit account/session changes;
    /// they must not poll the account endpoint on every background tick.
    /// </summary>
    internal DingTalkIdentityDto? CurrentIdentity { get; private set; }

    internal void ClearCachedIdentity() => SetCurrentIdentity(null, notify: false);

    internal event Action? IdentityChanged;

    internal DingTalkAccountService(IAuthenticatedAccountApi api) => _api = api;

    internal async Task<DingTalkIdentityDto?> GetAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _api.SendAsync(
            HttpMethod.Get, "dingtalk/me.php", null, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        DingTalkIdentityResponse? body = await JsonSerializer.DeserializeAsync(
            stream, DingTalkAccountJsonContext.Default.DingTalkIdentityResponse, cancellationToken);
        DingTalkIdentityDto? identity = body?.Identity;
        SetCurrentIdentity(identity, notify: false);
        return identity;
    }

    internal async Task<DingTalkIdentityDto> LinkAsync(
        DingTalkProfile profile,
        CancellationToken cancellationToken = default)
    {
        var request = new DingTalkIdentityRequest
        {
            CorpId = profile.CorpId,
            DingtalkUserId = profile.DingtalkUserId,
            DisplayName = profile.DisplayName,
            OrganizationName = profile.OrganizationName,
            DepartmentName = profile.DepartmentName,
            AvatarUrl = profile.AvatarUrl
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(request, DingTalkAccountJsonContext.Default.DingTalkIdentityRequest);
        using HttpResponseMessage response = await _api.SendAsync(
            HttpMethod.Post, "dingtalk/link.php", json, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        DingTalkIdentityResponse? body = await JsonSerializer.DeserializeAsync(
            stream, DingTalkAccountJsonContext.Default.DingTalkIdentityResponse, cancellationToken);
        DingTalkIdentityDto identity = body?.Identity ?? throw new InvalidDataException("The account service returned no DingTalk identity.");
        SetCurrentIdentity(identity, notify: true);
        return identity;
    }

    internal async Task UnlinkAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _api.SendAsync(
            HttpMethod.Post, "dingtalk/unlink.php", "{}"u8.ToArray(), null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        SetCurrentIdentity(null, notify: true);
    }

    private void SetCurrentIdentity(DingTalkIdentityDto? identity, bool notify)
    {
        bool changed = !AreEquivalent(CurrentIdentity, identity);
        CurrentIdentity = identity;
        if (notify && changed) IdentityChanged?.Invoke();
    }

    private static bool AreEquivalent(DingTalkIdentityDto? left, DingTalkIdentityDto? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return string.Equals(left.CorpId, right.CorpId, StringComparison.Ordinal)
            && string.Equals(left.DingtalkUserId, right.DingtalkUserId, StringComparison.Ordinal)
            && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
            && string.Equals(left.OrganizationName, right.OrganizationName, StringComparison.Ordinal)
            && string.Equals(left.DepartmentName, right.DepartmentName, StringComparison.Ordinal)
            && string.Equals(left.AvatarUrl, right.AvatarUrl, StringComparison.Ordinal);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        string code = "server_error";
        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            AuthErrorResponse? error = await JsonSerializer.DeserializeAsync(
                stream, AuthJsonContext.Default.AuthErrorResponse, cancellationToken);
            code = error?.Code is { Length: > 0 } value ? value : code;
        }
        catch { }
        throw new AuthApiException(code, "The DingTalk account operation failed.", response.StatusCode);
    }
}
