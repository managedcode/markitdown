using System.Text.Json.Serialization;

namespace MarkItDown.Intelligence.Providers.Azure.VideoIndexer;

[JsonConverter(typeof(JsonStringEnumConverter<ArmAccessTokenPermission>))]
internal enum ArmAccessTokenPermission
{
    Reader,
    Contributor
}
