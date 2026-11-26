using System.Text.Json.Serialization;

namespace MarkItDown.Intelligence.Providers.Azure.VideoIndexer;

[JsonConverter(typeof(JsonStringEnumConverter<ArmAccessTokenScope>))]
internal enum ArmAccessTokenScope
{
    Account,
    Project
}
