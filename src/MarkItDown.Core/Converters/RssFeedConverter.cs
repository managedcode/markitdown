using System.Text;
using System.Xml;

namespace MarkItDown.Core.Converters;

/// <summary>
/// Converter for RSS and Atom feeds that creates structured Markdown with feed metadata and items.
/// </summary>
public sealed class RssFeedConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".rss", ".atom", ".xml"
    };

    private static readonly HashSet<string> AcceptedMimeTypePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/rss+xml", "application/atom+xml", "application/rdf+xml", "text/xml"
    };

    public int Priority => 120; // Higher priority than general XML converter for feeds

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var mimeType = streamInfo.MimeType?.ToLowerInvariant() ?? string.Empty;
        var extension = streamInfo.Extension?.ToLowerInvariant();

        // Check specific RSS/Atom mime types
        foreach (var prefix in AcceptedMimeTypePrefixes.Take(3)) // Only RSS/Atom specific ones
        {
            if (mimeType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Check extensions (but only if they suggest RSS/Atom)
        if (extension is ".rss" or ".atom")
            return true;

        return false;
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (!AcceptsInput(streamInfo))
        {
            // For XML files, check if they contain RSS/Atom content
            if (streamInfo.Extension?.ToLowerInvariant() == ".xml" && stream.CanSeek)
            {
                return CheckForFeedContent(stream);
            }
            return false;
        }

        return true;
    }

    private static bool CheckForFeedContent(Stream stream)
    {
        try
        {
            var originalPosition = stream.Position;
            stream.Position = 0;

            // Read a portion of the stream to check for feed indicators
            var buffer = new byte[2048];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            var content = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            stream.Position = originalPosition;

            // Look for RSS or Atom indicators
            return content.Contains("<rss", StringComparison.OrdinalIgnoreCase) ||
                   content.Contains("<feed", StringComparison.OrdinalIgnoreCase) ||
                   content.Contains("<rdf:RDF", StringComparison.OrdinalIgnoreCase) ||
                   content.Contains("xmlns=\"http://www.w3.org/2005/Atom\"") ||
                   content.Contains("xmlns=\"http://purl.org/rss/1.0/\"");
        }
        catch
        {
            if (stream.CanSeek)
                stream.Position = 0;
            return false;
        }
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            // Reset stream position
            if (stream.CanSeek)
                stream.Position = 0;

            // Load XML document
            using var reader = new StreamReader(stream, streamInfo.Charset ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var xmlContent = await reader.ReadToEndAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(xmlContent))
                return new DocumentConverterResult(string.Empty);

            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xmlContent);

            var markdown = new StringBuilder();

            // Determine feed type and process accordingly
            if (IsAtomFeed(xmlDoc))
            {
                ProcessAtomFeed(xmlDoc, markdown);
            }
            else if (IsRssFeed(xmlDoc))
            {
                ProcessRssFeed(xmlDoc, markdown);
            }
            else
            {
                throw new FileConversionException("Document does not appear to be a valid RSS or Atom feed");
            }

            var title = ExtractFeedTitle(xmlDoc) ?? ExtractTitleFromFileName(streamInfo.FileName) ?? "RSS/Atom Feed";

            return new DocumentConverterResult(
                markdown: markdown.ToString().TrimEnd(),
                title: title
            );
        }
        catch (XmlException ex)
        {
            throw new FileConversionException($"Invalid XML format in feed: {ex.Message}", ex);
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
        {
            throw new FileConversionException($"Failed to convert RSS/Atom feed: {ex.Message}", ex);
        }
    }

    private static bool IsAtomFeed(XmlDocument doc)
    {
        return doc.DocumentElement?.LocalName.Equals("feed", StringComparison.OrdinalIgnoreCase) == true &&
               doc.DocumentElement.NamespaceURI.Contains("w3.org/2005/Atom");
    }

    private static bool IsRssFeed(XmlDocument doc)
    {
        return doc.DocumentElement?.LocalName.Equals("rss", StringComparison.OrdinalIgnoreCase) == true ||
               doc.DocumentElement?.LocalName.Equals("RDF", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static void ProcessAtomFeed(XmlDocument doc, StringBuilder markdown)
    {
        var namespaceManager = new XmlNamespaceManager(doc.NameTable);
        namespaceManager.AddNamespace("atom", "http://www.w3.org/2005/Atom");

        var feedElement = doc.DocumentElement!;

        // Feed metadata
        var title = GetElementText(feedElement, "atom:title", namespaceManager) ?? "Atom Feed";
        markdown.AppendLine($"# {title}");
        markdown.AppendLine();

        var subtitle = GetElementText(feedElement, "atom:subtitle", namespaceManager);
        if (!string.IsNullOrEmpty(subtitle))
        {
            markdown.AppendLine($"**Subtitle:** {subtitle}");
            markdown.AppendLine();
        }

        var updated = GetElementText(feedElement, "atom:updated", namespaceManager);
        if (!string.IsNullOrEmpty(updated))
        {
            markdown.AppendLine($"**Last Updated:** {FormatDate(updated)}");
        }

        var link = GetElementAttribute(feedElement, "atom:link[@rel='alternate']", "href", namespaceManager);
        if (!string.IsNullOrEmpty(link))
        {
            markdown.AppendLine($"**Link:** [{link}]({link})");
        }

        var author = GetElementText(feedElement, "atom:author/atom:name", namespaceManager);
        if (!string.IsNullOrEmpty(author))
        {
            markdown.AppendLine($"**Author:** {author}");
        }

        markdown.AppendLine();

        // Process entries
        var entries = feedElement.SelectNodes("atom:entry", namespaceManager);
        if (entries != null && entries.Count > 0)
        {
            markdown.AppendLine("## Entries");
            markdown.AppendLine();

            foreach (XmlNode entry in entries)
            {
                ProcessAtomEntry(entry, markdown, namespaceManager);
            }
        }
    }

    private static void ProcessRssFeed(XmlDocument doc, StringBuilder markdown)
    {
        var channel = doc.SelectSingleNode("//channel");
        if (channel == null) return;

        // Feed metadata
        var title = GetChildElementText(channel, "title") ?? "RSS Feed";
        markdown.AppendLine($"# {title}");
        markdown.AppendLine();

        var description = GetChildElementText(channel, "description");
        if (!string.IsNullOrEmpty(description))
        {
            markdown.AppendLine($"**Description:** {description}");
            markdown.AppendLine();
        }

        var lastBuildDate = GetChildElementText(channel, "lastBuildDate");
        if (!string.IsNullOrEmpty(lastBuildDate))
        {
            markdown.AppendLine($"**Last Updated:** {FormatDate(lastBuildDate)}");
        }

        var link = GetChildElementText(channel, "link");
        if (!string.IsNullOrEmpty(link))
        {
            markdown.AppendLine($"**Link:** [{link}]({link})");
        }

        var managingEditor = GetChildElementText(channel, "managingEditor");
        if (!string.IsNullOrEmpty(managingEditor))
        {
            markdown.AppendLine($"**Managing Editor:** {managingEditor}");
        }

        markdown.AppendLine();

        // Process items
        var items = channel.SelectNodes("item");
        if (items != null && items.Count > 0)
        {
            markdown.AppendLine("## Items");
            markdown.AppendLine();

            foreach (XmlNode item in items)
            {
                ProcessRssItem(item, markdown);
            }
        }
    }

    private static void ProcessAtomEntry(XmlNode entry, StringBuilder markdown, XmlNamespaceManager namespaceManager)
    {
        var title = GetElementText(entry, "atom:title", namespaceManager);
        if (!string.IsNullOrEmpty(title))
        {
            var link = GetElementAttribute(entry, "atom:link[@rel='alternate']", "href", namespaceManager);
            if (!string.IsNullOrEmpty(link))
            {
                markdown.AppendLine($"### [{title}]({link})");
            }
            else
            {
                markdown.AppendLine($"### {title}");
            }
            markdown.AppendLine();
        }

        var published = GetElementText(entry, "atom:published", namespaceManager);
        if (!string.IsNullOrEmpty(published))
        {
            markdown.AppendLine($"**Published:** {FormatDate(published)}");
        }

        var updated = GetElementText(entry, "atom:updated", namespaceManager);
        if (!string.IsNullOrEmpty(updated) && updated != published)
        {
            markdown.AppendLine($"**Updated:** {FormatDate(updated)}");
        }

        var author = GetElementText(entry, "atom:author/atom:name", namespaceManager);
        if (!string.IsNullOrEmpty(author))
        {
            markdown.AppendLine($"**Author:** {author}");
        }

        var summary = GetElementText(entry, "atom:summary", namespaceManager);
        var content = GetElementText(entry, "atom:content", namespaceManager);

        if (!string.IsNullOrEmpty(content))
        {
            markdown.AppendLine();
            markdown.AppendLine(content);
        }
        else if (!string.IsNullOrEmpty(summary))
        {
            markdown.AppendLine();
            markdown.AppendLine(summary);
        }

        markdown.AppendLine();
        markdown.AppendLine("---");
        markdown.AppendLine();
    }

    private static void ProcessRssItem(XmlNode item, StringBuilder markdown)
    {
        var title = GetChildElementText(item, "title");
        if (!string.IsNullOrEmpty(title))
        {
            var link = GetChildElementText(item, "link");
            if (!string.IsNullOrEmpty(link))
            {
                markdown.AppendLine($"### [{title}]({link})");
            }
            else
            {
                markdown.AppendLine($"### {title}");
            }
            markdown.AppendLine();
        }

        var pubDate = GetChildElementText(item, "pubDate");
        if (!string.IsNullOrEmpty(pubDate))
        {
            markdown.AppendLine($"**Published:** {FormatDate(pubDate)}");
        }

        var author = GetChildElementText(item, "author");
        if (string.IsNullOrEmpty(author))
        {
            // Try Dublin Core creator element
            var dcCreator = item.SelectSingleNode("*[local-name()='creator']");
            author = dcCreator?.InnerText?.Trim();
        }
        if (!string.IsNullOrEmpty(author))
        {
            markdown.AppendLine($"**Author:** {author}");
        }

        var category = GetChildElementText(item, "category");
        if (!string.IsNullOrEmpty(category))
        {
            markdown.AppendLine($"**Category:** {category}");
        }

        var description = GetChildElementText(item, "description");
        if (!string.IsNullOrEmpty(description))
        {
            markdown.AppendLine();
            markdown.AppendLine(description);
        }

        markdown.AppendLine();
        markdown.AppendLine("---");
        markdown.AppendLine();
    }

    private static string? GetElementText(XmlNode node, string xpath, XmlNamespaceManager namespaceManager)
    {
        return node.SelectSingleNode(xpath, namespaceManager)?.InnerText?.Trim();
    }

    private static string? GetElementAttribute(XmlNode node, string xpath, string attributeName, XmlNamespaceManager namespaceManager)
    {
        return node.SelectSingleNode(xpath, namespaceManager)?.Attributes?[attributeName]?.Value?.Trim();
    }

    private static string? GetChildElementText(XmlNode node, string elementName)
    {
        return node.SelectSingleNode(elementName)?.InnerText?.Trim();
    }

    private static string FormatDate(string dateString)
    {
        if (DateTime.TryParse(dateString, out var date))
        {
            return date.ToString("yyyy-MM-dd HH:mm:ss");
        }
        return dateString;
    }

    private static string? ExtractFeedTitle(XmlDocument doc)
    {
        // Try Atom first
        var namespaceManager = new XmlNamespaceManager(doc.NameTable);
        namespaceManager.AddNamespace("atom", "http://www.w3.org/2005/Atom");
        var atomTitle = doc.SelectSingleNode("//atom:title", namespaceManager)?.InnerText?.Trim();
        if (!string.IsNullOrEmpty(atomTitle)) return atomTitle;

        // Try RSS
        var rssTitle = doc.SelectSingleNode("//channel/title")?.InnerText?.Trim();
        if (!string.IsNullOrEmpty(rssTitle)) return rssTitle;

        return null;
    }

    private static string? ExtractTitleFromFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrEmpty(nameWithoutExtension) ? null : nameWithoutExtension;
    }
}