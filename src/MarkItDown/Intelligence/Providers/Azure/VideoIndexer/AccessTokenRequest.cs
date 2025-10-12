using System.Text.Json.Serialization;

namespace MarkItDown.Intelligence.Providers.Azure.VideoIndexer;

internal sealed class AccessTokenRequest
{
    [JsonPropertyName("permissionType")]
    public ArmAccessTokenPermission PermissionType { get; init; }

    [JsonPropertyName("scope")]
    public ArmAccessTokenScope Scope { get; init; }
}
