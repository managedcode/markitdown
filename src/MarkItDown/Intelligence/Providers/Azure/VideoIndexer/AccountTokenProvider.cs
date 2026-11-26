using System.Net.Http.Json;

namespace MarkItDown.Intelligence.Providers.Azure.VideoIndexer;

internal static class AccountTokenProvider
{
    public static async Task<AccountAccessToken> GenerateAccountAccessTokenAsync(HttpClient httpClient, string armAccessToken, string resourceId, ArmAccessTokenPermission permission, ArmAccessTokenScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

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

        var token = await response.Content.ReadFromJsonAsync<GenerateAccessTokenResponse>(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
        {
            throw new InvalidOperationException("Azure Video Indexer did not return an access token.");
        }

        return new AccountAccessToken(token.AccessToken, token.ExpirationTime);
    }
}

internal readonly record struct AccountAccessToken(string Token, DateTimeOffset? ExpiresOn)
{
    public bool IsExpired(DateTimeOffset now) => ExpiresOn.HasValue && now >= ExpiresOn.Value.Subtract(TimeSpan.FromMinutes(5));
}
