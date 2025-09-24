using System.Collections.Generic;
using System.Text;
using System.Xml;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for XML files that creates structured Markdown.
/// </summary>
public sealed class XmlConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xml", ".xsd", ".xsl", ".xslt", ".rss", ".atom"
    };

    private static readonly IReadOnlyCollection<string> AcceptedMimeTypePrefixes = new List<string>
    {
        MimeTypeUtilities.WithType(MimeHelper.XML, "application"),
        MimeHelper.XML,
        MimeHelper.RSS,
        MimeHelper.ATOM,
    };

    public int Priority => 300; // More specific than plain text

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

        // Try to parse a small portion of the stream to validate it's XML
        if (!stream.CanSeek)
            return true; // Fallback to mime type/extension check

        try
        {
            var originalPosition = stream.Position;
            stream.Position = 0;

            // Read a small chunk to check for XML declaration
            var buffer = new byte[1024];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            var content = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            stream.Position = originalPosition;

            // Simple check for XML-like content
            return content.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
                   content.Contains("<") && content.Contains(">");
        }
        catch
        {
            // If we can't read or parse, fall back to mime type check
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

            // Load XML document
            using var reader = new StreamReader(stream, streamInfo.Charset ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var xmlContent = await reader.ReadToEndAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(xmlContent))
                return new DocumentConverterResult(string.Empty);

            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xmlContent);

            var markdown = new StringBuilder();
            var title = ExtractTitleFromXml(xmlDoc) ?? ExtractTitleFromFileName(streamInfo.FileName);

            if (!string.IsNullOrEmpty(title))
            {
                markdown.AppendLine($"# {title}");
                markdown.AppendLine();
            }

            // Convert XML to structured markdown
            ConvertXmlNodeToMarkdown(xmlDoc.DocumentElement!, markdown, 0);

            return new DocumentConverterResult(
                markdown: markdown.ToString().TrimEnd(),
                title: title
            );
        }
        catch (XmlException ex)
        {
            throw new FileConversionException($"Invalid XML format: {ex.Message}", ex);
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
        {
            throw new FileConversionException($"Failed to convert XML file: {ex.Message}", ex);
        }
    }

    private static void ConvertXmlNodeToMarkdown(XmlNode node, StringBuilder markdown, int depth)
    {
        if (node == null) return;

        var indent = new string(' ', depth * 2);

        switch (node.NodeType)
        {
            case XmlNodeType.Element:
                // Add element as a heading or bold text depending on depth
                if (depth == 0)
                {
                    if (!string.IsNullOrEmpty(node.LocalName))
                    {
                        markdown.AppendLine($"## {FormatElementName(node.LocalName)}");
                        markdown.AppendLine();
                    }
                }
                else if (depth <= 3)
                {
                    var headingLevel = Math.Min(depth + 2, 6); // Max heading level is 6
                    markdown.AppendLine($"{new string('#', headingLevel)} {FormatElementName(node.LocalName)}");
                    markdown.AppendLine();
                }
                else
                {
                    markdown.AppendLine($"{indent}**{FormatElementName(node.LocalName)}**");
                    markdown.AppendLine();
                }

                // Add attributes if present
                if (node.Attributes?.Count > 0)
                {
                    foreach (XmlAttribute attr in node.Attributes)
                    {
                        markdown.AppendLine($"{indent}- **{FormatElementName(attr.Name)}**: {attr.Value}");
                    }
                    markdown.AppendLine();
                }

                // Process child nodes
                foreach (XmlNode child in node.ChildNodes)
                {
                    ConvertXmlNodeToMarkdown(child, markdown, depth + 1);
                }
                break;

            case XmlNodeType.Text:
                var text = node.Value?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    markdown.AppendLine($"{indent}{text}");
                    markdown.AppendLine();
                }
                break;

            case XmlNodeType.CDATA:
                var cdataText = node.Value?.Trim();
                if (!string.IsNullOrEmpty(cdataText))
                {
                    markdown.AppendLine($"{indent}```");
                    markdown.AppendLine($"{indent}{cdataText}");
                    markdown.AppendLine($"{indent}```");
                    markdown.AppendLine();
                }
                break;

            case XmlNodeType.Comment:
                var comment = node.Value?.Trim();
                if (!string.IsNullOrEmpty(comment))
                {
                    markdown.AppendLine($"{indent}<!-- {comment} -->");
                    markdown.AppendLine();
                }
                break;
        }
    }

    private static string FormatElementName(string elementName)
    {
        if (string.IsNullOrEmpty(elementName))
            return elementName;

        // Convert camelCase or PascalCase to readable format
        var result = new StringBuilder();
        for (int i = 0; i < elementName.Length; i++)
        {
            if (i > 0 && char.IsUpper(elementName[i]))
            {
                result.Append(' ');
            }
            result.Append(i == 0 ? char.ToUpper(elementName[i]) : elementName[i]);
        }

        return result.ToString();
    }

    private static string? ExtractTitleFromXml(XmlDocument xmlDoc)
    {
        try
        {
            // Try common title elements
            var titleSelectors = new[]
            {
                "//title", "//name", "//label", "//header", "//h1",
                "//*[local-name()='title']", "//*[local-name()='name']"
            };

            foreach (var selector in titleSelectors)
            {
                var titleNode = xmlDoc.SelectSingleNode(selector);
                if (titleNode?.InnerText?.Trim() is { Length: > 0 } title)
                {
                    return title;
                }
            }

            // Fallback to root element name
            return xmlDoc.DocumentElement?.LocalName;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractTitleFromFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrEmpty(nameWithoutExtension) ? null : nameWithoutExtension;
    }
}
