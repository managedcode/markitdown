using System.Collections.Generic;
using System.IO.Compression;
using System.Text;
using System.Xml;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for EPUB files that extracts content from e-book files.
/// </summary>
public sealed class EpubConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".epub"
    };

    private static readonly IReadOnlyCollection<string> AcceptedMimeTypePrefixes = new List<string>
    {
        MimeTypeUtilities.WithSubtype(MimeHelper.EPUB, "epub"),
        MimeHelper.EPUB,
        MimeTypeUtilities.WithSubtype(MimeHelper.EPUB, "x-epub+zip"),
    };

    private static readonly Dictionary<string, string> MimeTypeMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".html", MimeHelper.HTML },
        { ".htm", MimeHelper.HTML },
        { ".xhtml", MimeHelper.XHTML },
        { ".xml", MimeHelper.XSD },
    };

    private readonly HtmlConverter _htmlConverter;

    public int Priority => 250; // More specific than plain text, less than HTML

    public EpubConverter()
    {
        _htmlConverter = new HtmlConverter();
    }

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = MimeTypeUtilities.NormalizeMime(streamInfo);
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && AcceptedExtensions.Contains(extension))
            return true;

        return MimeTypeUtilities.MatchesAny(normalizedMime, AcceptedMimeTypePrefixes)
            || MimeTypeUtilities.MatchesAny(streamInfo.MimeType, AcceptedMimeTypePrefixes);
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (!AcceptsInput(streamInfo))
            return false;

        // EPUB files are ZIP files, so check for ZIP signature
        if (!stream.CanSeek)
            return true;

        try
        {
            var originalPosition = stream.Position;
            stream.Position = 0;

            // Check for ZIP file signature (PK) and look for mimetype file
            var buffer = new byte[4];
            var bytesRead = stream.Read(buffer, 0, 4);
            
            stream.Position = originalPosition;

            // EPUB files are ZIP files starting with "PK"
            return bytesRead >= 2 && buffer[0] == 0x50 && buffer[1] == 0x4B;
        }
        catch
        {
            if (stream.CanSeek)
                stream.Position = 0;
            return true;
        }
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            // Reset stream position
            if (stream.CanSeek)
                stream.Position = 0;

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

            // Extract metadata
            var metadata = await ExtractMetadata(archive, cancellationToken);
            
            // Get content file order from spine
            var contentFiles = await GetContentFilesInOrder(archive, cancellationToken);

            // Convert content to markdown
            var markdownContent = new List<string>();

            // Add metadata as markdown
            if (metadata.Count > 0)
            {
                var metadataMarkdown = new StringBuilder();
                foreach (var (key, value) in metadata)
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        metadataMarkdown.AppendLine($"**{FormatMetadataKey(key)}:** {value}");
                    }
                }
                if (metadataMarkdown.Length > 0)
                {
                    markdownContent.Add(metadataMarkdown.ToString().TrimEnd());
                }
            }

            // Process content files
            foreach (var filePath in contentFiles)
            {
                try
                {
                    var entry = archive.GetEntry(filePath);
                    if (entry == null) continue;

                    using var entryStream = entry.Open();
                    using var memoryStream = new MemoryStream();
                    await entryStream.CopyToAsync(memoryStream, cancellationToken);
                    memoryStream.Position = 0;

                    var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
                    var mimeType = MimeTypeMapping.TryGetValue(extension, out var mt) ? mt : "text/html";

                    var fileStreamInfo = new StreamInfo(
                        mimeType: mimeType,
                        extension: extension,
                        fileName: entry.Name
                    );

                    if (_htmlConverter.Accepts(memoryStream, fileStreamInfo, cancellationToken))
                    {
                        memoryStream.Position = 0;
                        var result = await _htmlConverter.ConvertAsync(memoryStream, fileStreamInfo, cancellationToken);
                        
                        if (!string.IsNullOrWhiteSpace(result.Markdown))
                        {
                            markdownContent.Add(result.Markdown.Trim());
                        }
                    }
                }
                catch (Exception)
                {
                    // Skip files that can't be processed
                    continue;
                }
            }

            var finalMarkdown = string.Join("\n\n", markdownContent);
            var title = metadata.TryGetValue("title", out var titleValue) ? titleValue : 
                       ExtractTitleFromFileName(streamInfo.FileName);

            return new DocumentConverterResult(
                markdown: finalMarkdown,
                title: title
            );
        }
        catch (InvalidDataException ex)
        {
            throw new FileConversionException($"Invalid EPUB file format: {ex.Message}", ex);
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
        {
            throw new FileConversionException($"Failed to convert EPUB file: {ex.Message}", ex);
        }
    }

    private static async Task<Dictionary<string, string>> ExtractMetadata(ZipArchive archive, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>();

        try
        {
            // Find content.opf file path from container.xml
            var containerEntry = archive.GetEntry("META-INF/container.xml");
            if (containerEntry == null) return metadata;

            var opfPath = await GetOpfPath(containerEntry, cancellationToken);
            if (string.IsNullOrEmpty(opfPath)) return metadata;

            // Parse content.opf for metadata
            var opfEntry = archive.GetEntry(opfPath);
            if (opfEntry == null) return metadata;

            using var opfStream = opfEntry.Open();
            var opfDoc = new XmlDocument();
            opfDoc.Load(opfStream);

            // Extract Dublin Core metadata
            var namespaceManager = new XmlNamespaceManager(opfDoc.NameTable);
            namespaceManager.AddNamespace("dc", "http://purl.org/dc/elements/1.1/");
            namespaceManager.AddNamespace("opf", "http://www.idpf.org/2007/opf");

            metadata["title"] = GetFirstTextContent(opfDoc, "//dc:title", namespaceManager);
            metadata["author"] = string.Join(", ", GetAllTextContents(opfDoc, "//dc:creator", namespaceManager));
            metadata["language"] = GetFirstTextContent(opfDoc, "//dc:language", namespaceManager);
            metadata["publisher"] = GetFirstTextContent(opfDoc, "//dc:publisher", namespaceManager);
            metadata["date"] = GetFirstTextContent(opfDoc, "//dc:date", namespaceManager);
            metadata["description"] = GetFirstTextContent(opfDoc, "//dc:description", namespaceManager);
            metadata["identifier"] = GetFirstTextContent(opfDoc, "//dc:identifier", namespaceManager);
        }
        catch
        {
            // If metadata extraction fails, continue without it
        }

        return metadata;
    }

    private static Task<string?> GetOpfPath(ZipArchiveEntry containerEntry, CancellationToken cancellationToken)
    {
        try
        {
            using var containerStream = containerEntry.Open();
            var containerDoc = new XmlDocument();
            containerDoc.Load(containerStream);

            var rootFileNode = containerDoc.SelectSingleNode("//rootfile[@media-type='application/oebps-package+xml']");
            return Task.FromResult(rootFileNode?.Attributes?["full-path"]?.Value);
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    private static async Task<List<string>> GetContentFilesInOrder(ZipArchive archive, CancellationToken cancellationToken)
    {
        var contentFiles = new List<string>();

        try
        {
            // Find content.opf file path
            var containerEntry = archive.GetEntry("META-INF/container.xml");
            if (containerEntry == null) return contentFiles;

            var opfPath = await GetOpfPath(containerEntry, cancellationToken);
            if (string.IsNullOrEmpty(opfPath)) return contentFiles;

            var opfEntry = archive.GetEntry(opfPath);
            if (opfEntry == null) return contentFiles;

            using var opfStream = opfEntry.Open();
            var opfDoc = new XmlDocument();
            opfDoc.Load(opfStream);

            // Get base path for resolving relative paths
            var basePath = Path.GetDirectoryName(opfPath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(basePath) && !basePath.EndsWith("/"))
                basePath += "/";

            // Build manifest (id -> href mapping)
            var manifest = new Dictionary<string, string>();
            var manifestNodes = opfDoc.SelectNodes("//manifest/item");
            if (manifestNodes != null)
            {
                foreach (XmlNode item in manifestNodes)
                {
                    var id = item.Attributes?["id"]?.Value;
                    var href = item.Attributes?["href"]?.Value;
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(href))
                    {
                        var fullHref = string.IsNullOrEmpty(basePath) ? href : basePath + href;
                        manifest[id] = fullHref;
                    }
                }
            }

            // Get spine order
            var spineNodes = opfDoc.SelectNodes("//spine/itemref");
            if (spineNodes != null)
            {
                foreach (XmlNode itemRef in spineNodes)
                {
                    var idref = itemRef.Attributes?["idref"]?.Value;
                    if (!string.IsNullOrEmpty(idref) && manifest.TryGetValue(idref, out var filePath))
                    {
                        contentFiles.Add(filePath);
                    }
                }
            }
        }
        catch
        {
            // If spine parsing fails, fall back to basic file enumeration
            contentFiles.Clear();
            foreach (var entry in archive.Entries)
            {
                var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (extension == ".html" || extension == ".xhtml" || extension == ".htm")
                {
                    contentFiles.Add(entry.FullName);
                }
            }
            contentFiles.Sort();
        }

        return contentFiles;
    }

    private static string GetFirstTextContent(XmlDocument doc, string xpath, XmlNamespaceManager namespaceManager)
    {
        var node = doc.SelectSingleNode(xpath, namespaceManager);
        return node?.InnerText?.Trim() ?? string.Empty;
    }

    private static List<string> GetAllTextContents(XmlDocument doc, string xpath, XmlNamespaceManager namespaceManager)
    {
        var results = new List<string>();
        var nodes = doc.SelectNodes(xpath, namespaceManager);
        if (nodes != null)
        {
            foreach (XmlNode node in nodes)
            {
                var text = node.InnerText?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    results.Add(text);
                }
            }
        }
        return results;
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
            _ => key.Substring(0, 1).ToUpper() + key.Substring(1).ToLower()
        };
    }

    private static string? ExtractTitleFromFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrEmpty(nameWithoutExtension) ? null : nameWithoutExtension;
    }
}
