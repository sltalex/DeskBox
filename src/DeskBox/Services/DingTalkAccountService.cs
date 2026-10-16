using System.Net;
using System.Text.Json;

namespace DeskBox.Services;

public sealed class DingTalkAccountService
{
    private readonly IAuthenticatedAccountApi _api;

    internal DingTalkAccountService(IAuthenticatedAccountApi api) => _api = api;

    internal async Task<DingTalkIdentityDto?> GetAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _api.SendAsync(
            HttpMethod.Get, "dingtalk/me.php", null, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        DingTalkIdentityResponse? body = await JsonSerializer.DeserializeAsync(
            stream, DingTalkAccountJsonContext.Default.DingTalkIdentityResponse, cancellationToken);
        return body?.Identity;
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
        return body?.Identity ?? throw new InvalidDataException("The account service returned no DingTalk identity.");
    }

    internal async Task UnlinkAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _api.SendAsync(
            HttpMethod.Post, "dingtalk/unlink.php", "{}"u8.ToArray(), null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
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
