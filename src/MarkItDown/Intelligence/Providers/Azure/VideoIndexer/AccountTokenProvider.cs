using System.Net.Http.Json;
using System.Text.Json;

namespace MarkItDown.Intelligence.Providers.Azure.VideoIndexer;

internal static class AccountTokenProvider
{
    public static async Task<AccountAccessToken> GenerateAccountAccessTokenAsync(HttpClient httpClient, string armAccessToken, string resourceId, ArmAccessTokenPermission permission, ArmAccessTokenScope scope, CancellationToken cancellationToken)
    {
        if (httpClient is null)
        {
            throw new ArgumentNullException(nameof(httpClient));
        }

        var requestUri = new Uri($"{VideoIndexerApiConstants.ArmEndpoint}{resourceId}/generateAccessToken?api-version={VideoIndexerApiConstants.ApiVersion}");

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new AccessTokenRequest
            {
                PermissionType = permission,
                Scope = scope
            })
        };

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", armAccessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var tokenValue = root.TryGetProperty("accessToken", out var tokenElement)
            ? tokenElement.GetString()
            : null;

        DateTimeOffset? expires = null;
        if (root.TryGetProperty("expirationTime", out var expirationElement))
        {
            var expirationString = expirationElement.GetString();
            if (!string.IsNullOrWhiteSpace(expirationString) && DateTimeOffset.TryParse(expirationString, out var parsed))
            {
                expires = parsed;
            }
        }

        if (string.IsNullOrWhiteSpace(tokenValue))
        {
            throw new InvalidOperationException($"Azure Video Indexer did not return an access token. Payload: {json}");
        }

        return new AccountAccessToken(tokenValue!, expires);
    }
}

internal readonly record struct AccountAccessToken(string Token, DateTimeOffset? ExpiresOn)
{
    public bool IsExpired(DateTimeOffset now) => ExpiresOn.HasValue && now >= ExpiresOn.Value.Subtract(TimeSpan.FromMinutes(5));
}
