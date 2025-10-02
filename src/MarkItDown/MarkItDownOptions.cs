namespace MarkItDown;

/// <summary>
/// Configurable options for <see cref="MarkItDown"/> that mirror the flexibility of the Python implementation.
/// </summary>
public sealed record MarkItDownOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether built-in converters should be registered. Defaults to <see langword="true"/>.
    /// </summary>
    public bool EnableBuiltins { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether dynamically discovered plugin converters should be enabled.
    /// (Plugins are not yet supported in the C# port, the flag is reserved for future work.)
    /// </summary>
    public bool EnablePlugins { get; init; }

    /// <summary>
    /// Optional path to an <c>exiftool</c> executable used by <see cref="Converters.ImageConverter"/> for metadata extraction.
    /// </summary>
    public string? ExifToolPath { get; init; }

    /// <summary>
    /// Optional delegate used to generate image captions. When provided the delegate receives the raw image bytes,
    /// associated <see cref="StreamInfo"/>, and a cancellation token, and should return a Markdown-ready description.
    /// </summary>
    public Func<byte[], StreamInfo, CancellationToken, Task<string?>>? ImageCaptioner { get; init; }

    /// <summary>
    /// Optional delegate used to transcribe audio content into markdown text.
    /// </summary>
    public Func<byte[], StreamInfo, CancellationToken, Task<string?>>? AudioTranscriber { get; init; }

    /// <summary>
    /// Optional configuration for Azure Document Intelligence integration.
    /// </summary>
    public DocumentIntelligenceOptions? DocumentIntelligence { get; init; }

    /// <summary>
    /// Options that control how converter results are segmented.
    /// </summary>
    public SegmentOptions Segments { get; init; } = SegmentOptions.Default;
}
