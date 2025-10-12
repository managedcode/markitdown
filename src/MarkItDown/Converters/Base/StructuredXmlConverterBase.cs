using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

public abstract class StructuredXmlConverterBase : DocumentPipelineConverterBase
{
    private readonly SegmentOptions segmentOptions;
    private readonly IConversionPipeline conversionPipeline;

    protected StructuredXmlConverterBase(
        SegmentOptions? segmentOptions = null,
        IConversionPipeline? pipeline = null,
        int priority = 150)
        : base(priority)
    {
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
        conversionPipeline = pipeline ?? ConversionPipeline.Empty;
    }

    protected abstract IReadOnlyCollection<string> AcceptedExtensions { get; }

    protected abstract IReadOnlyCollection<string> AcceptedMimeTypes { get; }

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = streamInfo.ResolveMimeType();
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && AcceptedExtensions.Contains(extension))
        {
            return true;
        }

        if (StreamInfo.MatchesMime(normalizedMime, AcceptedMimeTypes))
        {
            return true;
        }

        return StreamInfo.MatchesMime(streamInfo.MimeType, AcceptedMimeTypes);
    }

    protected virtual SegmentOptions ResolveSegmentOptions()
        => ConversionContextAccessor.Current?.Segments ?? segmentOptions;

    protected virtual ArtifactStorageOptions ResolveStorageOptions()
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
            var defaultExtension = AcceptedExtensions.FirstOrDefault() ?? ".xml";
            await using var source = await MaterializeSourceAsync(stream, streamInfo, defaultExtension, cancellationToken).ConfigureAwait(false);

            workspace = ArtifactWorkspaceFactory.CreateWorkspace(streamInfo, effectiveSegments, storageOptions);

            if (storageOptions.CopySourceDocument)
            {
                var sourceExtension = Path.GetExtension(streamInfo.FileName);
                if (string.IsNullOrWhiteSpace(sourceExtension))
                {
                    sourceExtension = Path.GetExtension(source.FilePath);
                }

                if (string.IsNullOrWhiteSpace(sourceExtension))
                {
                    sourceExtension = defaultExtension;
                }

                var sourceFileName = FileNameSanitizer.BuildFileName(streamInfo.FileName, "source", sourceExtension);
                var sourceMime = streamInfo.ResolveMimeType() ?? MimeHelper.GetMimeType(sourceExtension) ?? MimeHelper.XML;
                storedSourcePath = workspace.PersistFile(sourceFileName, source.FilePath, sourceMime, cancellationToken);
            }

            var document = await LoadDocumentAsync(source.FilePath, cancellationToken).ConfigureAwait(false);
            var markdown = RenderMarkdown(document);
            var extraction = BuildExtraction(document, markdown, streamInfo);

            await conversionPipeline.ExecuteAsync(streamInfo, extraction.Artifacts, extraction.Segments, cancellationToken).ConfigureAwait(false);

            var generatedAt = DateTime.UtcNow;
            var titleHint = extraction.TitleHint;
            var meta = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, streamInfo, effectiveSegments, titleHint, generatedAt);
            var fallbackTitle = Path.GetFileNameWithoutExtension(streamInfo.FileName ?? streamInfo.LocalPath ?? streamInfo.Url) ?? "Document";
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

            foreach (var pair in extraction.Artifacts.Metadata)
            {
                metadata[pair.Key] = pair.Value;
            }

            if (!string.IsNullOrWhiteSpace(storedSourcePath))
            {
                metadata[MetadataKeys.WorkspaceSourceFile] = storedSourcePath;
            }

            if (storageOptions.PersistMarkdown)
            {
                var markdownFileName = FileNameSanitizer.BuildFileName(streamInfo.FileName, "document", ".md");
                storedMarkdownPath = workspace.PersistText(markdownFileName, meta.Markdown, MimeHelper.MARKDOWN, cancellationToken);
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

            throw new FileConversionException($"Failed to convert XML document: {ex.Message}", ex);
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

    protected virtual async Task<XDocument> LoadDocumentAsync(string filePath, CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Access = FileAccess.Read,
            Mode = FileMode.Open,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };

        using var stream = new FileStream(filePath, options);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return XDocument.Parse(content, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch (XmlException ex) when (IsUndeclaredPrefixError(ex))
        {
            var sanitized = SanitizeNamespaces(content);
            if (!ReferenceEquals(sanitized, content))
            {
                return XDocument.Parse(sanitized, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            }

            throw;
        }
    }

    private static bool IsUndeclaredPrefixError(XmlException ex)
        => ex.Message.Contains("undeclared prefix", StringComparison.OrdinalIgnoreCase);

    private static string SanitizeNamespaces(string content)
    {
        var sanitized = content;

        if (sanitized.Contains("xlink:", StringComparison.OrdinalIgnoreCase) &&
            !sanitized.Contains("xmlns:xlink", StringComparison.OrdinalIgnoreCase))
        {
            sanitized = InjectNamespaceDeclaration(sanitized, "xmlns:xlink=\"http://www.w3.org/1999/xlink\"");
        }

        return sanitized;
    }

    private static string InjectNamespaceDeclaration(string content, string declaration)
    {
        var index = FindRootTagStart(content);
        if (index < 0)
        {
            return content;
        }

        var closing = content.IndexOf('>', index);
        if (closing < 0)
        {
            return content;
        }

        return content.Insert(closing, " " + declaration);
    }

    private static int FindRootTagStart(string content)
    {
        var index = content.IndexOf('<');
        while (index >= 0 && index < content.Length - 1)
        {
            var next = content[index + 1];
            if (next == '?' || next == '!' || next == '/')
            {
                index = content.IndexOf('<', index + 1);
                continue;
            }

            return index;
        }

        return -1;
    }

    protected abstract string RenderMarkdown(XDocument document);

    protected virtual string? ExtractTitle(XDocument document)
    {
        return document.Root?.Element(document.Root.GetDefaultNamespace() + "title")?.Value
            ?? document.Root?.Element("title")?.Value;
    }

    internal static string RenderTextNodes(IEnumerable<XNode> nodes)
    {
        var builder = new StringBuilder();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case XText text:
                    builder.Append(text.Value);
                    break;
                case XElement element:
                    builder.Append(RenderElementInline(element));
                    break;
            }
        }

        return builder.ToString();
    }

    private static string RenderElementInline(XElement element)
    {
        var content = RenderTextNodes(element.Nodes());
        var name = element.Name.LocalName.ToLowerInvariant();
        return name switch
        {
            "emphasis" or "em" or "i" => $"*{content}*",
            "bold" or "strong" or "b" => $"**{content}**",
            "link" or "a" =>
                element.Attribute("href") is { } href ? $"[{content}]({href.Value})" : content,
            "sub" => $"~{content}~",
            "sup" => $"^{content}^",
            _ => content,
        };
    }

    protected virtual List<DocumentSegment> CreateSegments(string markdown, StreamInfo streamInfo)
    {
        var segments = new List<DocumentSegment>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return segments;
        }

        var metadata = new Dictionary<string, string>
        {
            [MetadataKeys.Page] = "1"
        };

        var segment = new DocumentSegment(
            markdown: markdown,
            type: SegmentType.Page,
            number: 1,
            label: "Page 1",
            source: streamInfo.FileName,
            additionalMetadata: metadata);

        segments.Add(segment);
        return segments;
    }

    protected virtual ConversionArtifacts CreateArtifacts(string markdown, IReadOnlyList<DocumentSegment> segments, StreamInfo streamInfo)
    {
        var artifacts = new ConversionArtifacts();
        if (!string.IsNullOrWhiteSpace(markdown) && segments.Count > 0)
        {
            artifacts.TextBlocks.Add(new TextArtifact(markdown, 1, streamInfo.FileName, segments[0].Label));
        }

        return artifacts;
    }

    private StructuredExtraction BuildExtraction(XDocument document, string markdown, StreamInfo streamInfo)
    {
        var normalized = TextSanitizer.Normalize(markdown, trim: true);
        var segments = CreateSegments(normalized, streamInfo);
        var artifacts = CreateArtifacts(normalized, segments, streamInfo);
        var titleHint = ExtractTitle(document);

        return new StructuredExtraction(segments, artifacts, titleHint);
    }

    private sealed record StructuredExtraction(
        List<DocumentSegment> Segments,
        ConversionArtifacts Artifacts,
        string? TitleHint);
}
