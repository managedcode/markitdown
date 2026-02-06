using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Providers.Aws;
using MarkItDown.Intelligence.Providers.Azure;
using MarkItDown.Intelligence.Providers.Google;
using Microsoft.Extensions.Logging;
using MarkItDown.YouTube;

namespace MarkItDown;

/// <summary>
/// Configurable options for <see cref="MarkItDownClient"/> that mirror the flexibility of the Python implementation.
/// </summary>
public sealed record MarkItDownOptions
{
    /// <summary>
    /// Optional root directory for all MarkItDown workspaces and buffers.
    /// Defaults to <c>.markitdown</c> under <see cref="Environment.CurrentDirectory"/>.
    /// Set to a writable path in read-only environments (e.g. Azure Functions temp).
    /// </summary>
    public string? RootPath { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether built-in converters should be registered. Defaults to <see langword="true"/>.
    /// </summary>
    public bool EnableBuiltins { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether dynamically discovered plugin converters should be enabled.
    /// (Plugins are not yet supported in the C# port, the flag is reserved for future work.)
    /// </summary>
    public bool EnablePlugins { get; set; }

    /// <summary>
    /// Optional path to an <c>exiftool</c> executable used by <see cref="Converters.ImageConverter"/> for metadata extraction.
    /// </summary>
    public string? ExifToolPath { get; set; }

    /// <summary>
    /// Optional delegate used to generate image captions. When provided the delegate receives the raw image bytes,
    /// associated <see cref="StreamInfo"/>, and a cancellation token, and should return a Markdown-ready description.
    /// </summary>
    public Func<byte[], StreamInfo, CancellationToken, Task<string?>>? ImageCaptioner { get; set; }

    /// <summary>
    /// Optional delegate used to transcribe audio content into markdown text.
    /// </summary>
    public Func<byte[], StreamInfo, CancellationToken, Task<string?>>? AudioTranscriber { get; set; }

    /// <summary>
    /// Optional configuration for legacy Azure Document Intelligence integration.
    /// </summary>
    public DocumentIntelligenceOptions? DocumentIntelligence { get; set; }

    /// <summary>
    /// Explicit intelligence providers to use for structured analysis. When <see langword="null"/> the runtime falls back to <see cref="AzureIntelligence"/> configuration.
    /// </summary>
    public IDocumentIntelligenceProvider? DocumentIntelligenceProvider { get; set; }

    /// <summary>
    /// Explicit image understanding provider. When <see langword="null"/> the runtime falls back to <see cref="AzureIntelligence"/> configuration.
    /// </summary>
    public IImageUnderstandingProvider? ImageUnderstandingProvider { get; set; }

    /// <summary>
    /// Explicit media transcription provider. When <see langword="null"/> the runtime falls back to <see cref="AzureIntelligence"/> configuration.
    /// </summary>
    public IMediaTranscriptionProvider? MediaTranscriptionProvider { get; set; }

    /// <summary>
    /// Optional Azure intelligence configuration used when explicit providers are not supplied.
    /// </summary>
    public AzureIntelligenceOptions? AzureIntelligence { get; set; }

    /// <summary>
    /// Optional Google Cloud intelligence configuration used when explicit providers are not supplied.
    /// </summary>
    public GoogleIntelligenceOptions? GoogleIntelligence { get; set; }

    /// <summary>
    /// Optional AWS intelligence configuration used when explicit providers are not supplied.
    /// </summary>
    public AwsIntelligenceOptions? AwsIntelligence { get; set; }

    /// <summary>
    /// Optional access to Microsoft.Extensions.AI clients.
    /// </summary>
    public IAiModelProvider? AiModels { get; set; }

    /// <summary>
    /// Optional provider used to resolve YouTube metadata and captions for <see cref="Converters.YouTubeUrlConverter"/>.
    /// </summary>
    public IYouTubeMetadataProvider? YouTubeMetadataProvider { get; set; }

    /// <summary>
    /// Options that control how converter results are segmented.
    /// </summary>
    public SegmentOptions Segments { get; set; } = SegmentOptions.Default;

    /// <summary>
    /// Custom middleware invoked after extraction but before Markdown composition.
    /// </summary>
    public IReadOnlyList<IConversionMiddleware> ConversionMiddleware { get; set; } = Array.Empty<IConversionMiddleware>();

    /// <summary>
    /// Controls how per-document workspaces and artifacts are persisted via <see cref="ManagedCode.Storage.Core.IStorage"/>.
    /// </summary>
    public ArtifactStorageOptions ArtifactStorage { get; set; } = ArtifactStorageOptions.Default;

    /// <summary>
    /// Gets or sets a value indicating whether AI-based image enrichment should be enabled when a chat client is present.
    /// </summary>
    public bool EnableAiImageEnrichment { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether OpenTelemetry instrumentation should be emitted. Defaults to <see langword="true"/>.
    /// </summary>
    public bool EnableTelemetry { get; set; } = true;

    /// <summary>
    /// Optional <see cref="ActivitySource"/> to use when creating telemetry spans. When <see langword="null"/> a shared source is used.
    /// </summary>
    public ActivitySource? ActivitySource { get; set; }

    /// <summary>
    /// Optional <see cref="Meter"/> used to emit metric counters. When <see langword="null"/> a shared meter is used.
    /// </summary>
    public Meter? Meter { get; set; }

    /// <summary>
    /// Optional logger factory used when explicit loggers are not supplied to <see cref="MarkItDownClient"/>.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// Controls how much detail is emitted via progress callbacks.
    /// </summary>
    public ProgressDetailLevel ProgressDetail { get; set; } = ProgressDetailLevel.Basic;
}
