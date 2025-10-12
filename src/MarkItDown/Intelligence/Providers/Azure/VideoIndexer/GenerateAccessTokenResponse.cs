using System.Text.Json.Serialization;

namespace MarkItDown.Intelligence.Providers.Azure.VideoIndexer;

internal sealed class GenerateAccessTokenResponse
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("accessTokenType")]
    public string? AccessTokenType { get; init; }

    [JsonPropertyName("expirationTime")]
    public DateTimeOffset? ExpirationTime { get; init; }
}
