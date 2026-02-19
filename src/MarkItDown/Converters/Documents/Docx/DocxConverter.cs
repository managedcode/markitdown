using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using DocumentFormat.OpenXml.Wordprocessing;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for Microsoft Word (.docx) files to Markdown using DocumentFormat.OpenXml.
/// </summary>
public sealed partial class DocxConverter : WordprocessingDocumentConverterBase
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx"
    };

    private static readonly HashSet<string> AcceptedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    private readonly SegmentOptions segmentOptions;
    private readonly IConversionPipeline conversionPipeline;
    private readonly IImageUnderstandingProvider? imageUnderstandingProvider;
    private readonly IDocumentIntelligenceProvider? documentIntelligenceProvider;
    private readonly IAiModelProvider aiModels;

    public DocxConverter(
        SegmentOptions? segmentOptions = null,
        IConversionPipeline? pipeline = null,
        IImageUnderstandingProvider? imageProvider = null,
        IDocumentIntelligenceProvider? documentProvider = null,
        IAiModelProvider? aiModelProvider = null)
        : base(priority: 210)
    {
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
        conversionPipeline = pipeline ?? ConversionPipeline.Empty;
        imageUnderstandingProvider = this.segmentOptions.Image.EnableImageUnderstandingProvider ? imageProvider : null;
        documentIntelligenceProvider = this.segmentOptions.Image.EnableDocumentIntelligence ? documentProvider : null;
        aiModels = aiModelProvider ?? NullAiModelProvider.Instance;
    }

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var mimeType = streamInfo.MimeType?.ToLowerInvariant() ?? string.Empty;
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && AcceptedExtensions.Contains(extension))
            return true;

        if (AcceptedMimeTypes.Contains(mimeType))
            return true;

        return false;
    }

    public override bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (!AcceptsInput(streamInfo))
            return false;

        if (stream.CanSeek && stream.Length > 4)
        {
            var originalPosition = stream.Position;
            try
            {
                stream.Position = 0;
                Span<byte> buffer = stackalloc byte[4];
                var bytesRead = stream.Read(buffer);
                stream.Position = originalPosition;

                if (bytesRead == 4)
                {
                    return buffer[0] == 0x50 && buffer[1] == 0x4B &&
                           (buffer[2] == 0x03 || buffer[2] == 0x05 || buffer[2] == 0x07) &&
                           (buffer[3] == 0x04 || buffer[3] == 0x06 || buffer[3] == 0x08);
                }
            }
            catch
            {
                stream.Position = originalPosition;
            }
        }

        return true;
    }

    public override async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        ArtifactWorkspace? artifactWorkspace = null;
        ArtifactStorageOptions storageOptions = ResolveStorageOptions();
        string? storedSourcePath = null;
        string? storedMarkdownPath = null;

        try
        {
            await using var source = await MaterializeSourceAsync(stream, streamInfo, ".docx", cancellationToken).ConfigureAwait(false);

            var effectiveSegments = ResolveSegmentOptions();
            artifactWorkspace = ArtifactWorkspaceFactory.CreateWorkspace(streamInfo, effectiveSegments, storageOptions);

            if (storageOptions.CopySourceDocument)
            {
                var sourceExtension = Path.GetExtension(streamInfo.FileName);
                if (string.IsNullOrWhiteSpace(sourceExtension))
                {
                    sourceExtension = Path.GetExtension(source.FilePath);
                }

                if (string.IsNullOrWhiteSpace(sourceExtension))
                {
                    sourceExtension = ".docx";
                }

                var sourceFileName = FileNameSanitizer.BuildFileName(streamInfo.FileName, "source", sourceExtension);
                var sourceMime = streamInfo.ResolveMimeType() ?? MimeHelper.DOCX;
                storedSourcePath = artifactWorkspace.PersistFile(sourceFileName, source.FilePath, sourceMime, cancellationToken);
            }

            var imagePersistor = new ImageArtifactPersistor(artifactWorkspace, streamInfo);

            var localExtraction = await ExtractDocumentAsync(source.FilePath, streamInfo, imagePersistor, effectiveSegments, cancellationToken).ConfigureAwait(false);
            DocumentIntelligenceResult? analysis = null;
            if (effectiveSegments.Image.EnableDocumentIntelligence)
            {
                analysis = await AnalyzeWithDocumentIntelligenceAsync(source.FilePath, streamInfo, cancellationToken).ConfigureAwait(false);
            }

            var extraction = analysis is not null
                ? await BuildExtractionFromDocumentIntelligenceAsync(analysis, localExtraction, streamInfo, cancellationToken).ConfigureAwait(false)
                : localExtraction;

            await conversionPipeline.ExecuteAsync(streamInfo, extraction.Artifacts, extraction.Segments, cancellationToken).ConfigureAwait(false);

            var titleHint = ExtractTitle(extraction.RawText);
            var generatedAt = DateTime.UtcNow;
            var meta = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, streamInfo, effectiveSegments, titleHint, generatedAt);
            var title = meta.Title
                ?? titleHint
                ?? Path.GetFileNameWithoutExtension(streamInfo.FileName ?? streamInfo.LocalPath ?? streamInfo.Url)
                ?? "Document";
            var pageCount = extraction.Segments.Count(static segment => segment.Type == SegmentType.Page);
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataKeys.DocumentPages] = pageCount.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentImages] = extraction.Artifacts.Images.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentTables] = extraction.Artifacts.Tables.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.WorkspaceDirectory] = artifactWorkspace.DirectoryPath
            };

            if (!string.IsNullOrWhiteSpace(titleHint))
            {
                metadata[MetadataKeys.DocumentTitleHint] = titleHint!;
            }

            metadata[MetadataKeys.DocumentTitle] = title;

            if (!string.IsNullOrWhiteSpace(storedSourcePath))
            {
                metadata[MetadataKeys.WorkspaceSourceFile] = storedSourcePath;
            }

            if (storageOptions.PersistMarkdown)
            {
                var markdownFileName = FileNameSanitizer.BuildFileName(streamInfo.FileName, "document", ".md");
                storedMarkdownPath = artifactWorkspace.PersistText(markdownFileName, meta.Markdown, MimeHelper.MARKDOWN, cancellationToken);
                metadata[MetadataKeys.WorkspaceMarkdownFile] = storedMarkdownPath;
            }

            var markdownFactory = () =>
            {
                var recomposed = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, streamInfo, effectiveSegments, titleHint, generatedAt);
                return recomposed.Markdown;
            };

            return DocumentConverterResult.FromFactory(
                markdownFactory,
                title,
                extraction.Segments,
                extraction.Artifacts,
                metadata,
                artifactDirectory: artifactWorkspace.DirectoryPath,
                cleanup: null,
                asyncCleanup: artifactWorkspace,
                generatedAtUtc: generatedAt);
        }
        catch (Exception ex) when (ex is not MarkItDownException)
        {
            if (artifactWorkspace is not null)
            {
                await artifactWorkspace.DisposeAsync().ConfigureAwait(false);
            }
            throw new FileConversionException($"Failed to convert DOCX file: {ex.Message}", ex);
        }
        catch
        {
            if (artifactWorkspace is not null)
            {
                await artifactWorkspace.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private SegmentOptions ResolveSegmentOptions()
        => ConversionContextAccessor.Current?.Segments ?? segmentOptions;

    private static ArtifactStorageOptions ResolveStorageOptions()
        => ConversionContextAccessor.Current?.Storage ?? ArtifactStorageOptions.Default;

    private static int GetGridSpan(TableCell cell)
    {
        var span = cell.TableCellProperties?.GridSpan?.Val;
        return span is not null && span.HasValue ? span.Value : 1;
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = TextSanitizer.Normalize(text, trim: false);
        while (normalized.Contains("\n\n\n", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        }

        return normalized.Trim();
    }

    private static string EscapeMarkdownTableCell(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = TextSanitizer.Normalize(text, trim: true, collapseWhitespaceAroundLineBreaks: false);
        normalized = normalized.Replace("|", "\\|", StringComparison.Ordinal);
        return normalized.Replace("\n", "<br />", StringComparison.Ordinal);
    }

    private static string? ExtractTitle(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return null;

        var lines = markdown.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        static string? TryExtract(IEnumerable<string> candidates, bool allowFallback)
        {
            var insideComment = false;

            foreach (var rawLine in candidates)
            {
                var trimmedLine = rawLine.Trim();
                if (trimmedLine.Length == 0)
                {
                    continue;
                }

                if (insideComment)
                {
                    if (trimmedLine.Contains("-->", StringComparison.Ordinal))
                    {
                        insideComment = false;
                    }
                    continue;
                }

                if (trimmedLine.StartsWith("<!--", StringComparison.Ordinal))
                {
                    insideComment = !trimmedLine.Contains("-->", StringComparison.Ordinal);
                    continue;
                }

                if (trimmedLine.StartsWith('>') || trimmedLine.StartsWith("![", StringComparison.Ordinal))
                {
                    continue;
                }

                if (trimmedLine.StartsWith('#'))
                {
                    var candidate = trimmedLine.TrimStart('#').Trim();
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        return candidate;
                    }
                    continue;
                }

                if (allowFallback && trimmedLine.Length > 5 && trimmedLine.Length < 100)
                {
                    return trimmedLine;
                }
            }

            return null;
        }

        var preview = lines.Take(10).ToArray();
        return TryExtract(preview, allowFallback: false) ?? TryExtract(preview, allowFallback: true);
    }
}
