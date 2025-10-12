namespace MarkItDown;

/// <summary>
/// Common OpenTelemetry tag names used across MarkItDown instrumentation.
/// </summary>
internal static class TelemetryTags
{
    public const string Converter = "markitdown.converter";
    public const string Base64Length = "markitdown.base64.length";
    public const string ExceptionType = "exception.type";
    public const string ExceptionMessage = "exception.message";
    public const string ActivityStatusDescription = "markitdown.status.description";
    public const string Mime = "markitdown.mime";
    public const string Extension = "markitdown.extension";
    public const string FileName = "markitdown.filename";
    public const string Url = "markitdown.url";
    public const string Path = "markitdown.path";
}
