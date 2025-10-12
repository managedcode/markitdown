using System;

namespace MarkItDown;

/// <summary>
/// Options that control how segmented markdown output is composed.
/// </summary>
public sealed record SegmentOptions
{
    /// <summary>
    /// Controls whether segment metadata annotations should be emitted inline with markdown output.
    /// </summary>
    public bool IncludeSegmentMetadataInMarkdown { get; init; }

    /// <summary>
    /// Converter-specific options for audio content.
    /// </summary>
    public AudioSegmentOptions Audio { get; init; } = AudioSegmentOptions.Default;

    /// <summary>
    /// Converter-specific options for image content.
    /// </summary>
    public ImageSegmentOptions Image { get; init; } = ImageSegmentOptions.Default;

    /// <summary>
    /// Converter-specific options for PDF content.
    /// </summary>
    public PdfSegmentOptions Pdf { get; init; } = PdfSegmentOptions.Default;

    private static readonly int DefaultMaxParallelImageAnalysis = Math.Max(Environment.ProcessorCount * 4, 32);
    private int maxParallelImageAnalysis = DefaultMaxParallelImageAnalysis;

    /// <summary>
    /// Maximum number of concurrent image analysis operations per document. Must be a positive integer.
    /// </summary>
    public int MaxParallelImageAnalysis
    {
        get => maxParallelImageAnalysis;
        init => maxParallelImageAnalysis = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "MaxParallelImageAnalysis must be positive.");
    }

    /// <summary>
    /// Provides a default instance.
    /// </summary>
    public static SegmentOptions Default { get; } = new();
}

/// <summary>
/// Options related to segmenting audio (and other timed media) content.
/// </summary>
public sealed record AudioSegmentOptions
{
    /// <summary>
    /// Duration used when slicing audio transcripts into segments. Defaults to one minute.
    /// </summary>
    public TimeSpan SegmentDuration { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Provides a default instance.
    /// </summary>
    public static AudioSegmentOptions Default { get; } = new();
}

/// <summary>
/// Options for controlling image analysis and enrichment.
/// </summary>
public sealed record ImageSegmentOptions
{
    /// <summary>
    /// Enables Azure Document Intelligence enrichment when available. Set to <c>false</c> to skip remote parsing.
    /// </summary>
    public bool EnableDocumentIntelligence { get; init; } = true;

    /// <summary>
    /// Enables the configured <see cref="IImageUnderstandingProvider"/> for image caption and OCR extraction.
    /// </summary>
    public bool EnableImageUnderstandingProvider { get; init; } = true;

    /// <summary>
    /// Enables downstream AI chat enrichment (for example, GPT-based descriptions via <see cref="Microsoft.Extensions.AI.IChatClient"/>).
    /// </summary>
    public bool EnableAiEnrichment { get; init; } = true;

    /// <summary>
    /// Optional directory where extracted image artifacts should be persisted. When <see langword="null"/> a temporary workspace is used.
    /// </summary>
    public string? ArtifactDirectory { get; init; }

    /// <summary>
    /// When <see langword="true"/>, keeps auto-generated artifact directories on disk after the conversion result is disposed. Explicit directories are never deleted automatically.
    /// </summary>
    public bool KeepArtifactDirectory { get; init; }

    /// <summary>
    /// Provides a default instance.
    /// </summary>
    public static ImageSegmentOptions Default { get; } = new();
}

/// <summary>
/// Options for controlling PDF extraction behaviour.
/// </summary>
public sealed record PdfSegmentOptions
{
    /// <summary>
    /// When <see langword="true"/>, every PDF page is rendered as an image first and passed through OCR/vision pipelines before Markdown composition.
    /// </summary>
    public bool TreatPagesAsImages { get; init; }

    /// <summary>
    /// Provides a default instance.
    /// </summary>
    public static PdfSegmentOptions Default { get; } = new();
}
