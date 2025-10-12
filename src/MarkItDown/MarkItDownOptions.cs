using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Providers.Aws;
using MarkItDown.Intelligence.Providers.Azure;
using MarkItDown.Intelligence.Providers.Google;
using MarkItDown.YouTube;

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
    /// Optional configuration for legacy Azure Document Intelligence integration.
    /// </summary>
    public DocumentIntelligenceOptions? DocumentIntelligence { get; init; }

    /// <summary>
    /// Explicit intelligence providers to use for structured analysis. When <see langword="null"/> the runtime falls back to <see cref="AzureIntelligence"/> configuration.
    /// </summary>
    public IDocumentIntelligenceProvider? DocumentIntelligenceProvider { get; init; }

    /// <summary>
    /// Explicit image understanding provider. When <see langword="null"/> the runtime falls back to <see cref="AzureIntelligence"/> configuration.
    /// </summary>
    public IImageUnderstandingProvider? ImageUnderstandingProvider { get; init; }

    /// <summary>
    /// Explicit media transcription provider. When <see langword="null"/> the runtime falls back to <see cref="AzureIntelligence"/> configuration.
    /// </summary>
    public IMediaTranscriptionProvider? MediaTranscriptionProvider { get; init; }

    /// <summary>
    /// Optional Azure intelligence configuration used when explicit providers are not supplied.
    /// </summary>
    public AzureIntelligenceOptions? AzureIntelligence { get; init; }

    /// <summary>
    /// Optional Google Cloud intelligence configuration used when explicit providers are not supplied.
    /// </summary>
    public GoogleIntelligenceOptions? GoogleIntelligence { get; init; }

    /// <summary>
    /// Optional AWS intelligence configuration used when explicit providers are not supplied.
    /// </summary>
    public AwsIntelligenceOptions? AwsIntelligence { get; init; }

    /// <summary>
    /// Optional access to Microsoft.Extensions.AI clients.
    /// </summary>
    public IAiModelProvider? AiModels { get; init; }

    /// <summary>
    /// Optional provider used to resolve YouTube metadata and captions for <see cref="Converters.YouTubeUrlConverter"/>.
    /// </summary>
    public IYouTubeMetadataProvider? YouTubeMetadataProvider { get; init; }

    /// <summary>
    /// Options that control how converter results are segmented.
    /// </summary>
    public SegmentOptions Segments { get; init; } = SegmentOptions.Default;
}
