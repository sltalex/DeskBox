namespace DeskBox.Services;

/// <summary>
/// Sends an account-scoped request without exposing bearer credentials to feature services.
/// Implementations refresh and replay an unauthorized request at most once.
/// </summary>
internal interface IAuthenticatedAccountApi
{
    Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativePath,
        byte[]? jsonBody,
        string? contentEncoding,
        CancellationToken cancellationToken = default);
}
