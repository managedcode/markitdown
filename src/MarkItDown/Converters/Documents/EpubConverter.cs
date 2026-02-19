using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ManagedCode.MimeTypes;
using MarkItDown;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for EPUB files that extracts structured content from e-book archives.
/// </summary>
public sealed class EpubConverter : DocumentPipelineConverterBase
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".epub"
    };

    private static readonly IReadOnlyCollection<string> AcceptedMimeTypePrefixes = new[]
    {
        "application/epub",
        MimeHelper.EPUB,
        "application/x-epub+zip",
    };

    private static readonly Dictionary<string, string> MimeTypeMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = MimeHelper.HTML,
        [".htm"] = MimeHelper.HTML,
        [".xhtml"] = MimeHelper.XHTML,
        [".xml"] = MimeHelper.XSD,
    };

    private readonly HtmlConverter htmlConverter;
    private readonly SegmentOptions segmentOptions;
    private readonly IConversionPipeline conversionPipeline;

    public EpubConverter(SegmentOptions? segmentOptions = null, IConversionPipeline? pipeline = null, HtmlConverter? htmlConverter = null)
        : base(priority: 250)
    {
        this.segmentOptions = segmentOptions ?? SegmentOptions.Default;
        conversionPipeline = pipeline ?? ConversionPipeline.Empty;
        this.htmlConverter = htmlConverter ?? new HtmlConverter();
    }

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = streamInfo.ResolveMimeType();
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && AcceptedExtensions.Contains(extension))
        {
            return true;
        }

        return StreamInfo.MatchesMime(normalizedMime, AcceptedMimeTypePrefixes) ||
               StreamInfo.MatchesMime(streamInfo.MimeType, AcceptedMimeTypePrefixes);
    }

    private SegmentOptions ResolveSegmentOptions()
        => ConversionContextAccessor.Current?.Segments ?? segmentOptions;

    private static ArtifactStorageOptions ResolveStorageOptions()
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
            await using var source = await MaterializeSourceAsync(stream, streamInfo, ".epub", cancellationToken).ConfigureAwait(false);

            workspace = WorkspacePersistence.CreateWorkspace(streamInfo, effectiveSegments, storageOptions, source.FilePath, ".epub", MimeHelper.EPUB, cancellationToken, out storedSourcePath);

            await using var fileStream = OpenReadOnlyFile(source.FilePath);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);

            var metadata = await ExtractMetadataAsync(archive, cancellationToken).ConfigureAwait(false);
            var contentFiles = await GetContentFilesInOrderAsync(archive, cancellationToken).ConfigureAwait(false);
            var sections = await ConvertContentToSectionsAsync(archive, contentFiles, cancellationToken).ConfigureAwait(false);

            if (metadata.Count > 0)
            {
                sections.Insert(0, BuildMetadataSection(metadata));
            }

            var extraction = BuildExtraction(sections, streamInfo);

            foreach (var pair in metadata)
            {
                extraction.Artifacts.Metadata[$"epub.{pair.Key}"] = pair.Value;
            }

            await conversionPipeline.ExecuteAsync(streamInfo, extraction.Artifacts, extraction.Segments, cancellationToken).ConfigureAwait(false);

            metadata.TryGetValue("title", out var titleHint);
            var generatedAt = DateTime.UtcNow;
            var meta = SegmentMarkdownComposer.Compose(extraction.Segments, extraction.Artifacts, streamInfo, effectiveSegments, titleHint, generatedAt);
            var fallbackTitle = titleHint ?? ExtractTitleFromFileName(streamInfo.FileName) ?? "EPUB Document";
            var title = meta.Title ?? titleHint ?? fallbackTitle;

            var documentMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataKeys.DocumentTitle] = title,
                [MetadataKeys.DocumentPages] = extraction.Segments.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentImages] = extraction.Artifacts.Images.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.DocumentTables] = extraction.Artifacts.Tables.Count.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.WorkspaceDirectory] = workspace.DirectoryPath
            };

            foreach (var pair in extraction.Artifacts.Metadata)
            {
                documentMetadata[pair.Key] = pair.Value;
            }

            if (!string.IsNullOrWhiteSpace(storedSourcePath))
            {
                documentMetadata[MetadataKeys.WorkspaceSourceFile] = storedSourcePath;
            }

            storedMarkdownPath = WorkspacePersistence.PersistMarkdown(workspace, storageOptions, streamInfo, meta.Markdown, cancellationToken);
            if (!string.IsNullOrWhiteSpace(storedMarkdownPath))
            {
                documentMetadata[MetadataKeys.WorkspaceMarkdownFile] = storedMarkdownPath;
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
                documentMetadata,
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

            throw new FileConversionException($"Failed to convert EPUB file: {ex.Message}", ex);
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

    private static SectionContent BuildMetadataSection(IReadOnlyDictionary<string, string> metadata)
    {
        var builder = new StringBuilder();
        foreach (var pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            builder.AppendLine($"**{FormatMetadataKey(pair.Key)}:** {pair.Value}");
        }

        return new SectionContent("Metadata", builder.ToString().TrimEnd(), SegmentType.Metadata);
    }

    private async Task<List<SectionContent>> ConvertContentToSectionsAsync(
        ZipArchive archive,
        IReadOnlyList<string> orderedFiles,
        CancellationToken cancellationToken)
    {
        var sections = new List<SectionContent>();
        var index = 0;

        foreach (var filePath in orderedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = archive.GetEntry(filePath);
            if (entry is null)
            {
                continue;
            }

            var normalized = await RenderSectionMarkdownAsync(entry, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            sections.Add(new SectionContent($"Section {++index}", normalized, SegmentType.Chapter));
        }

        return sections;
    }

    private async Task<string?> RenderSectionMarkdownAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var entryStream = entry.Open();
        await using var bufferHandle = await DiskBufferHandle.FromStreamAsync(entryStream, Path.GetExtension(entry.Name), bufferSize: 128 * 1024, onChunkWritten: null, cancellationToken).ConfigureAwait(false);

        var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
        var mimeType = MimeTypeMapping.TryGetValue(extension, out var mappedMime)
            ? mappedMime
            : MimeHelper.HTML;

        var sectionStreamInfo = new StreamInfo(
            fileName: entry.Name,
            extension: extension,
            mimeType: mimeType);

        await using (var detectionStream = bufferHandle.OpenRead())
        {
            if (!htmlConverter.Accepts(detectionStream, sectionStreamInfo, cancellationToken))
            {
                return null;
            }
        }

        await using var conversionStream = bufferHandle.OpenRead();
        await using var htmlResult = await htmlConverter.ConvertAsync(conversionStream, sectionStreamInfo, cancellationToken).ConfigureAwait(false);
        return TextSanitizer.Normalize(htmlResult.Markdown, trim: true);
    }

    private static async Task<Dictionary<string, string>> ExtractMetadataAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var containerEntry = archive.GetEntry("META-INF/container.xml");
        if (containerEntry is null)
        {
            return metadata;
        }

        var opfPath = await GetOpfPathAsync(containerEntry, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(opfPath))
        {
            return metadata;
        }

        var opfEntry = archive.GetEntry(opfPath);
        if (opfEntry is null)
        {
            return metadata;
        }

        var document = new XmlDocument();
        await using (var opfStream = opfEntry.Open())
        {
            document.Load(opfStream);
        }

        var manager = BuildNamespaceManager(document);
        metadata[MetadataKeys.EpubTitle] = GetFirstTextContent(document, "//opf:metadata/dc:title", manager);
        metadata[MetadataKeys.EpubAuthor] = string.Join(", ", GetAllTextContents(document, "//opf:metadata/dc:creator", manager));
        metadata[MetadataKeys.EpubLanguage] = GetFirstTextContent(document, "//opf:metadata/dc:language", manager);
        metadata[MetadataKeys.EpubPublisher] = GetFirstTextContent(document, "//opf:metadata/dc:publisher", manager);
        metadata[MetadataKeys.EpubDate] = GetFirstTextContent(document, "//opf:metadata/dc:date", manager);
        metadata[MetadataKeys.EpubDescription] = GetFirstTextContent(document, "//opf:metadata/dc:description", manager);
        metadata[MetadataKeys.EpubIdentifier] = GetFirstTextContent(document, "//opf:metadata/dc:identifier", manager);

        return metadata;
    }

    private static XmlNamespaceManager BuildNamespaceManager(XmlDocument doc)
    {
        var manager = new XmlNamespaceManager(doc.NameTable);
        manager.AddNamespace("opf", "http://www.idpf.org/2007/opf");
        manager.AddNamespace("dc", "http://purl.org/dc/elements/1.1/");
        return manager;
    }

    private static async Task<string?> GetOpfPathAsync(ZipArchiveEntry containerEntry, CancellationToken cancellationToken)
    {
        try
        {
            var container = new XmlDocument();
            await using var stream = containerEntry.Open();
            container.Load(stream);

            var node = container.SelectSingleNode("/container/rootfiles/rootfile");
            var path = node?.Attributes?["full-path"]?.Value;
            return string.IsNullOrWhiteSpace(path) ? null : path.Replace("\\", "/");
        }
        catch
        {
            return null;
        }
    }

    private static async Task<List<string>> GetContentFilesInOrderAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var files = new List<string>();

        try
        {
            var containerEntry = archive.GetEntry("META-INF/container.xml");
            if (containerEntry is null)
            {
                return files;
            }

            var opfPath = await GetOpfPathAsync(containerEntry, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(opfPath))
            {
                return files;
            }

            var opfEntry = archive.GetEntry(opfPath);
            if (opfEntry is null)
            {
                return files;
            }

            var document = new XmlDocument();
            await using (var opfStream = opfEntry.Open())
            {
                document.Load(opfStream);
            }

            var manager = BuildNamespaceManager(document);
            var manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var basePath = Path.GetDirectoryName(opfPath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(basePath) && !basePath.EndsWith('/'))
            {
                basePath += "/";
            }

            var manifestNodes = document.SelectNodes("//opf:manifest/opf:item", manager);
            if (manifestNodes is not null)
            {
                foreach (XmlNode item in manifestNodes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var id = item.Attributes?["id"]?.Value;
                    var href = item.Attributes?["href"]?.Value;
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(href))
                    {
                        continue;
                    }

                    var fullPath = string.IsNullOrEmpty(basePath) ? href : basePath + href;
                    manifest[id] = fullPath.Replace("\\", "/");
                }
            }

            var spineNodes = document.SelectNodes("//opf:spine/opf:itemref", manager);
            if (spineNodes is not null)
            {
                foreach (XmlNode itemRef in spineNodes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var idRef = itemRef.Attributes?["idref"]?.Value;
                    if (!string.IsNullOrWhiteSpace(idRef) && manifest.TryGetValue(idRef, out var path))
                    {
                        files.Add(path);
                    }
                }
            }
        }
        catch
        {
            files.Clear();
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (extension is ".html" or ".htm" or ".xhtml")
                {
                    files.Add(entry.FullName);
                }
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
        }

        return files;
    }

    private static string GetFirstTextContent(XmlDocument doc, string xpath, XmlNamespaceManager namespaceManager)
        => doc.SelectSingleNode(xpath, namespaceManager)?.InnerText?.Trim() ?? string.Empty;

    private static IReadOnlyList<string> GetAllTextContents(XmlDocument doc, string xpath, XmlNamespaceManager namespaceManager)
    {
        var list = new List<string>();
        var nodes = doc.SelectNodes(xpath, namespaceManager);
        if (nodes is null)
        {
            return list;
        }

        foreach (XmlNode node in nodes)
        {
            var value = node.InnerText?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                list.Add(value);
            }
        }

        return list;
    }

    private static string FormatMetadataKey(string key)
    {
        return key switch
        {
            "author" => "Author",
            "title" => "Title",
            "publisher" => "Publisher",
            "date" => "Publication Date",
            "language" => "Language",
            "description" => "Description",
            "identifier" => "Identifier",
            _ => char.ToUpperInvariant(key[0]) + key[1..]
        };
    }

    private static string? ExtractTitleFromFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static EpubExtractionResult BuildExtraction(IReadOnlyList<SectionContent> sections, StreamInfo streamInfo)
    {
        var segments = new List<DocumentSegment>();
        var artifacts = new ConversionArtifacts();

        var index = 0;
        foreach (var section in sections)
        {
            var normalized = TextSanitizer.Normalize(section.Markdown, trim: true);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var pageNumber = ++index;
            var metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Page] = pageNumber.ToString(CultureInfo.InvariantCulture),
                [MetadataKeys.EpubSection] = section.Label
            };

            var segment = new DocumentSegment(
                normalized,
                section.Type,
                pageNumber,
                section.Type == SegmentType.Metadata ? section.Label : $"Page {pageNumber}",
                source: streamInfo.FileName,
                additionalMetadata: metadata);

            segments.Add(segment);
            artifacts.TextBlocks.Add(new TextArtifact(normalized, pageNumber, streamInfo.FileName, segment.Label));
        }

        if (segments.Count == 0)
        {
            var emptySegment = new DocumentSegment(string.Empty, SegmentType.Page, 1, "Page 1", source: streamInfo.FileName);
            segments.Add(emptySegment);
        }

        return new EpubExtractionResult(segments, artifacts);
    }

    private sealed record SectionContent(string Label, string Markdown, SegmentType Type);

    private sealed record EpubExtractionResult(List<DocumentSegment> Segments, ConversionArtifacts Artifacts);
}
