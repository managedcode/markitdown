using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MarkItDown;

/// <summary>
/// The result of converting a document to Markdown.
/// </summary>
public sealed class DocumentConverterResult : IDisposable, IAsyncDisposable
{
    private static readonly IReadOnlyList<DocumentSegment> EmptySegments =
        new ReadOnlyCollection<DocumentSegment>(Array.Empty<DocumentSegment>());

    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    private static readonly string[] MetadataPrefixesToDiscard =
    {
        "ai.",
        "artifact.",
        "converter.",
        "image."
    };

    private static readonly HashSet<string> MetadataKeysToDiscard = new(StringComparer.OrdinalIgnoreCase)
    {
        MetadataKeys.ImageEnriched,
        MetadataKeys.ModelVersion,
        MetadataKeys.LayoutRegions,
        MetadataKeys.InteractionElements,
        MetadataKeys.Highlights,
        MetadataKeys.DataVisuals,
        MetadataKeys.StructuredTables,
        MetadataKeys.MermaidDiagram,
        MetadataKeys.Caption,
        MetadataKeys.DetailedDescription,
        MetadataKeys.OcrText,
        MetadataKeys.ArtifactPath
    };

    private readonly Func<string> markdownAccessor;
    private readonly IDisposable? cleanupHandle;
    private readonly IAsyncDisposable? asyncCleanupHandle;
    private int disposeState;

    /// <summary>
    /// Initialize the <see cref="DocumentConverterResult"/> with a dynamic Markdown factory.
    /// </summary>
    /// <param name="markdownFactory">Factory invoked whenever the Markdown payload is requested.</param>
    /// <param name="title">Optional title of the document.</param>
    /// <param name="segments">Optional collection of segments that represent structured slices of the output.</param>
    /// <param name="artifacts">Optional raw extraction artifacts available prior to Markdown composition.</param>
    /// <param name="metadata">Optional metadata describing the conversion.</param>
    /// <param name="artifactDirectory">Optional directory containing persisted artifacts (images, etc.).</param>
    /// <param name="cleanup">Optional synchronous cleanup handle invoked when the result is disposed.</param>
    /// <param name="asyncCleanup">Optional asynchronous cleanup handle invoked when the result is disposed.</param>
    /// <param name="generatedAtUtc">Optional timestamp representing when the Markdown output was produced.</param>
    private DocumentConverterResult(
        Func<string> markdownFactory,
        string? title = null,
        IReadOnlyList<DocumentSegment>? segments = null,
        ConversionArtifacts? artifacts = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? artifactDirectory = null,
        IDisposable? cleanup = null,
        IAsyncDisposable? asyncCleanup = null,
        DateTime? generatedAtUtc = null)
    {
        markdownAccessor = markdownFactory ?? throw new ArgumentNullException(nameof(markdownFactory));
        Title = title;
        Segments = segments is null
            ? EmptySegments
            : new ReadOnlyCollection<DocumentSegment>(segments.ToArray());
        Artifacts = artifacts ?? ConversionArtifacts.Empty;
        Metadata = NormalizeMetadata(metadata);
        ArtifactDirectory = string.IsNullOrWhiteSpace(artifactDirectory) ? null : artifactDirectory;
        cleanupHandle = cleanup;
        asyncCleanupHandle = asyncCleanup;
        GeneratedAtUtc = generatedAtUtc ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Initialize the <see cref="DocumentConverterResult"/> with a static Markdown payload.
    /// </summary>
    public DocumentConverterResult(
        string markdown,
        string? title = null,
        IReadOnlyList<DocumentSegment>? segments = null,
        ConversionArtifacts? artifacts = null,
        IReadOnlyDictionary<string, string>? metadata = null)
        : this(CreateStaticMarkdownFactory(markdown), title, segments, artifacts, metadata)
    {
    }

    public static DocumentConverterResult FromFactory(
        Func<string> markdownFactory,
        string? title = null,
        IReadOnlyList<DocumentSegment>? segments = null,
        ConversionArtifacts? artifacts = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? artifactDirectory = null,
        IDisposable? cleanup = null,
        IAsyncDisposable? asyncCleanup = null,
        DateTime? generatedAtUtc = null)
        => new DocumentConverterResult(markdownFactory, title, segments, artifacts, metadata, artifactDirectory, cleanup, asyncCleanup, generatedAtUtc);

    private static Func<string> CreateStaticMarkdownFactory(string markdown)
    {
        if (markdown is null)
        {
            throw new ArgumentNullException(nameof(markdown));
        }
        return () => markdown;
    }

    /// <summary>
    /// The converted Markdown text (composed on demand).
    /// </summary>
    public string Markdown => markdownAccessor();

    /// <summary>
    /// Optional title of the document.
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// Segmented view of the converted output.
    /// </summary>
    public IReadOnlyList<DocumentSegment> Segments { get; }

    /// <summary>
    /// Raw extraction artifacts captured during conversion.
    /// </summary>
    public ConversionArtifacts Artifacts { get; }

    /// <summary>
    /// Conversion metadata such as timings or usage statistics.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Absolute directory path that holds persisted artifacts for this conversion (when available).
    /// </summary>
    public string? ArtifactDirectory { get; }

    /// <summary>
    /// Timestamp (UTC) associated with the composed Markdown payload.
    /// </summary>
    public DateTime GeneratedAtUtc { get; }

    /// <summary>
    /// Return the converted Markdown text.
    /// </summary>
    public override string ToString() => Markdown;

    /// <summary>
    /// Create a new <see cref="DocumentConverterResult"/> with metadata merged with the provided dictionary.
    /// </summary>
    public DocumentConverterResult WithMetadata(IReadOnlyDictionary<string, string> additionalMetadata)
    {
        if (additionalMetadata is null || additionalMetadata.Count == 0)
        {
            return this;
        }

        var merged = MergeMetadata(Metadata, additionalMetadata);
        return new DocumentConverterResult(
            markdownAccessor,
            Title,
            Segments,
            Artifacts,
            merged,
            ArtifactDirectory,
            cleanupHandle,
            asyncCleanupHandle,
            GeneratedAtUtc);
    }

    /// <summary>
    /// Dispose the conversion result and release any artifact resources.
    /// </summary>
    public void Dispose()
    {
        DisposeAsyncCore().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Asynchronously dispose the conversion result and release any artifact resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeAsyncCore()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
        {
            return;
        }

        if (asyncCleanupHandle is not null)
        {
            await asyncCleanupHandle.DisposeAsync().ConfigureAwait(false);
            return;
        }

        cleanupHandle?.Dispose();
    }

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return EmptyMetadata;
        }

        if (metadata is ReadOnlyDictionary<string, string> readOnly)
        {
            return readOnly;
        }

        var normalized = new Dictionary<string, string>(metadata.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            var key = pair.Key.Trim();
            if (IsInternalMetadataKey(key))
            {
                continue;
            }

            var value = TextSanitizer.Normalize(pair.Value, trim: true);
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            normalized[key] = value;
        }

        if (normalized.Count == 0)
        {
            return EmptyMetadata;
        }

        return new ReadOnlyDictionary<string, string>(normalized);
    }

    private static IReadOnlyDictionary<string, string> MergeMetadata(IReadOnlyDictionary<string, string> baseline, IReadOnlyDictionary<string, string> additions)
    {
        if (baseline is null)
        {
            baseline = EmptyMetadata;
        }

        var normalizedAdditions = NormalizeMetadata(additions);
        if (normalizedAdditions.Count == 0)
        {
            return baseline;
        }

        if (baseline == EmptyMetadata)
        {
            return normalizedAdditions;
        }

        var merged = new Dictionary<string, string>(baseline.Count + normalizedAdditions.Count, StringComparer.OrdinalIgnoreCase);

        if (baseline != EmptyMetadata)
        {
            foreach (var pair in baseline)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        foreach (var pair in normalizedAdditions)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            merged[pair.Key] = pair.Value ?? string.Empty;
        }

        return merged.Count == 0
            ? EmptyMetadata
            : new ReadOnlyDictionary<string, string>(merged);
    }

    private static bool IsInternalMetadataKey(string key)
    {
        foreach (var prefix in MetadataPrefixesToDiscard)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return MetadataKeysToDiscard.Contains(key);
    }
}
