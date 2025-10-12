using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ManagedCode.MimeTypes;
using MarkItDown;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for OpenDocument Text (ODT) packages.
/// </summary>
public sealed class OdtConverter : DocumentPipelineConverterBase
{
    private const string ContentEntryName = "content.xml";

    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".odt",
    };

    private static readonly string OdtMime = MimeHelper.GetMimeType(".odt")
        ?? "application/vnd.oasis.opendocument.text";

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        OdtMime,
    };

    private readonly SegmentOptions segmentOptions;
    private readonly IConversionPipeline conversionPipeline;

    public OdtConverter(SegmentOptions? segmentOptions = null, IConversionPipeline? pipeline = null)
        : base(priority: 180)
    {
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
        conversionPipeline = pipeline ?? ConversionPipeline.Empty;
    }

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = streamInfo.ResolveMimeType();
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && Extensions.Contains(extension))
        {
            return true;
        }

        return StreamInfo.MatchesMime(normalizedMime, MimeTypes)
            || StreamInfo.MatchesMime(streamInfo.MimeType, MimeTypes);
    }

    private SegmentOptions ResolveSegmentOptions()
        => ConversionContextAccessor.Current?.Segments ?? segmentOptions;

    private ArtifactStorageOptions ResolveStorageOptions()
        => ConversionContextAccessor.Current?.Storage ?? ArtifactStorageOptions.Default;

    public override async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        ArtifactWorkspace? workspace = null;
        var storageOptions = ResolveStorageOptions();
        var effectiveSegments = ResolveSegmentOptions();
        string? storedSourcePath = null;
        string? storedMarkdownPath = null;

        try
        {
            await using var source = await MaterializeSourceAsync(stream, streamInfo, ".odt", cancellationToken).ConfigureAwait(false);

            workspace = WorkspacePersistence.CreateWorkspace(streamInfo, effectiveSegments, storageOptions, source.FilePath, ".odt", OdtMime, cancellationToken, out storedSourcePath);

            await using var fileStream = OpenReadOnlyFile(source.FilePath);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);
            var entry = archive.GetEntry(ContentEntryName);
            if (entry is null)
            {
                var emptySegments = new List<DocumentSegment>
                {
                    new DocumentSegment(string.Empty, SegmentType.Page, 1, "Page 1", source: streamInfo.FileName)
                };
                var generatedAtEmpty = DateTime.UtcNow;
                var titleEmpty = ExtractTitleFromFileName(streamInfo.FileName) ?? "ODT Document";
                var metadataEmpty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [MetadataKeys.DocumentTitle] = titleEmpty,
                    [MetadataKeys.DocumentPages] = "0",
                    [MetadataKeys.DocumentImages] = "0",
                    [MetadataKeys.DocumentTables] = "0",
                    [MetadataKeys.WorkspaceDirectory] = workspace.DirectoryPath
                };

                if (!string.IsNullOrWhiteSpace(storedSourcePath))
                {
                    metadataEmpty[MetadataKeys.WorkspaceSourceFile] = storedSourcePath;
                }

                storedMarkdownPath = WorkspacePersistence.PersistMarkdown(workspace, storageOptions, streamInfo, string.Empty, cancellationToken);
                if (!string.IsNullOrWhiteSpace(storedMarkdownPath))
                {
                    metadataEmpty[MetadataKeys.WorkspaceMarkdownFile] = storedMarkdownPath;
                }

                return DocumentConverterResult.FromFactory(
                    () => string.Empty,
                    titleEmpty,
                    emptySegments,
                    new ConversionArtifacts(),
                    metadataEmpty,
                    artifactDirectory: workspace.DirectoryPath,
                    cleanup: null,
                    asyncCleanup: workspace,
                    generatedAtUtc: generatedAtEmpty);
            }

            await using var entryStream = entry.Open();
            var document = await XDocument.LoadAsync(entryStream, LoadOptions.PreserveWhitespace, cancellationToken).ConfigureAwait(false);

            var markdown = RenderMarkdown(document);
            var extraction = BuildExtraction(document, markdown, streamInfo);

            await conversionPipeline.ExecuteAsync(streamInfo, extraction.Artifacts, extraction.Segments, cancellationToken).ConfigureAwait(false);

            var generatedAt = DateTime.UtcNow;
            var titleHint = ExtractTitle(document);
            var meta = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, streamInfo, effectiveSegments, titleHint, generatedAt);
            var fallbackTitle = ExtractTitleFromFileName(streamInfo.FileName) ?? "ODT Document";
            var title = meta.Title ?? titleHint ?? fallbackTitle;

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataKeys.DocumentTitle] = title,
                [MetadataKeys.DocumentPages] = extraction.Segments.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentImages] = extraction.Artifacts.Images.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentTables] = extraction.Artifacts.Tables.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.WorkspaceDirectory] = workspace.DirectoryPath
            };

            if (!string.IsNullOrWhiteSpace(titleHint))
            {
                metadata[MetadataKeys.DocumentTitleHint] = titleHint!;
            }

            if (!string.IsNullOrWhiteSpace(storedSourcePath))
            {
                metadata[MetadataKeys.WorkspaceSourceFile] = storedSourcePath;
            }

            storedMarkdownPath = WorkspacePersistence.PersistMarkdown(workspace, storageOptions, streamInfo, meta.Markdown, cancellationToken);
            if (!string.IsNullOrWhiteSpace(storedMarkdownPath))
            {
                metadata[MetadataKeys.WorkspaceMarkdownFile] = storedMarkdownPath;
            }

            string MarkdownFactory()
            {
                var recomposed = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, streamInfo, effectiveSegments, titleHint, generatedAt);
                return recomposed.Markdown;
            }

            return DocumentConverterResult.FromFactory(
                MarkdownFactory,
                title,
                extraction.Segments,
                extraction.Artifacts,
                metadata,
                artifactDirectory: workspace.DirectoryPath,
                cleanup: null,
                asyncCleanup: workspace,
                generatedAtUtc: generatedAt);
        }
        catch (Exception ex) when (ex is not MarkItDownException)
        {
            if (workspace is not null)
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
            }

            throw new FileConversionException($"Failed to convert ODT file: {ex.Message}", ex);
        }
        catch
        {
            if (workspace is not null)
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private static string RenderMarkdown(XDocument document)
    {
        var builder = new StringBuilder();
        var officeNs = document.Root?.Name.Namespace ?? XNamespace.None;
        var textNs = (XNamespace)"urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        var body = document.Root?
            .Element(officeNs + "body")?
            .Element(officeNs + "text");

        if (body is null)
        {
            return string.Empty;
        }

        foreach (var node in body.Elements())
        {
            var localName = node.Name.LocalName;
            if (node.Name == textNs + "h")
            {
                var levelAttribute = node.Attribute(textNs + "outline-level");
                var level = 1;
                if (levelAttribute is not null && int.TryParse(levelAttribute.Value, out var parsed))
                {
                    level = Math.Clamp(parsed, 1, 6);
                }

                builder.AppendLine(new string('#', level) + " " + StructuredXmlConverterBase.RenderTextNodes(node.Nodes()));
                builder.AppendLine();
            }
            else if (node.Name == textNs + "p")
            {
                var text = StructuredXmlConverterBase.RenderTextNodes(node.Nodes());
                if (!string.IsNullOrWhiteSpace(text))
                {
                    builder.AppendLine(text.Trim());
                    builder.AppendLine();
                }
            }
        }

        return builder.ToString().Trim();
    }

    private OdtExtractionResult BuildExtraction(XDocument document, string markdown, StreamInfo streamInfo)
    {
        var normalized = TextSanitizer.Normalize(markdown, trim: true);
        var segments = new List<DocumentSegment>();
        var artifacts = new ConversionArtifacts();

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Page] = "1"
            };

            var segment = new DocumentSegment(
                markdown: normalized,
                type: SegmentType.Page,
                number: 1,
                label: "Page 1",
                source: streamInfo.FileName,
                additionalMetadata: metadata);

            segments.Add(segment);
            artifacts.TextBlocks.Add(new TextArtifact(normalized, 1, streamInfo.FileName, segment.Label));
        }

        return new OdtExtractionResult(segments, artifacts);
    }

    private static string? ExtractTitle(XDocument document)
    {
        var officeNs = document.Root?.Name.Namespace ?? XNamespace.None;
        var metaNs = (XNamespace)"urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
        return document.Root?
            .Element(officeNs + "meta")?
            .Element(metaNs + "title")?
            .Value;
    }

    private static string? ExtractTitleFromFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(withoutExtension) ? null : withoutExtension;
    }

    private sealed record OdtExtractionResult(List<DocumentSegment> Segments, ConversionArtifacts Artifacts);
}
