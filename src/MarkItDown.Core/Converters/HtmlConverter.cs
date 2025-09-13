using HtmlAgilityPack;
using System.Text;

namespace MarkItDown.Core.Converters;

/// <summary>
/// Converter for HTML files to Markdown using HtmlAgilityPack.
/// </summary>
public sealed class HtmlConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".xhtml"
    };

    private static readonly HashSet<string> AcceptedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/html", "application/xhtml+xml"
    };

    public int Priority => 100; // Specific format converter, high priority

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var mimeType = streamInfo.MimeType?.ToLowerInvariant() ?? string.Empty;
        var extension = streamInfo.Extension?.ToLowerInvariant();

        // Check the extension
        if (extension is not null && AcceptedExtensions.Contains(extension))
            return true;

        // Check the mimetype
        if (AcceptedMimeTypes.Contains(mimeType))
            return true;

        return false;
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        return AcceptsInput(streamInfo);
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        string htmlContent;

        if (streamInfo.Charset is not null)
        {
            using var reader = new StreamReader(stream, streamInfo.Charset, leaveOpen: true);
            htmlContent = await reader.ReadToEndAsync(cancellationToken);
        }
        else
        {
            // Try to detect encoding
            var buffer = new byte[stream.Length];
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            
            if (bytesRead > 0)
            {
                // Try UTF-8 first, then fall back to system default
                try
                {
                    htmlContent = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    // Check if it's valid UTF-8
                    if (htmlContent.Contains('\ufffd') && buffer.Length > 3)
                    {
                        htmlContent = Encoding.Default.GetString(buffer, 0, bytesRead);
                    }
                }
                catch
                {
                    htmlContent = Encoding.Default.GetString(buffer, 0, bytesRead);
                }
            }
            else
            {
                htmlContent = string.Empty;
            }
        }

        return new DocumentConverterResult(ConvertHtmlToMarkdown(htmlContent), ExtractTitle(htmlContent));
    }

    private static string ConvertHtmlToMarkdown(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var markdown = new StringBuilder();
        
        // Process the entire document - start from the document node
        ConvertNodeToMarkdown(doc.DocumentNode, markdown, 0);

        var result = markdown.ToString().Trim();
        
        // Clean up excessive newlines
        while (result.Contains("\n\n\n"))
        {
            result = result.Replace("\n\n\n", "\n\n");
        }
        
        return result;
    }

    private static string? ExtractTitle(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Try to get title from <title> tag
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        if (titleNode is not null && !string.IsNullOrWhiteSpace(titleNode.InnerText))
        {
            return HtmlEntity.DeEntitize(titleNode.InnerText.Trim());
        }

        // Try to get title from first h1
        var h1Node = doc.DocumentNode.SelectSingleNode("//h1");
        if (h1Node is not null && !string.IsNullOrWhiteSpace(h1Node.InnerText))
        {
            return HtmlEntity.DeEntitize(h1Node.InnerText.Trim());
        }

        return null;
    }

    private static void ConvertNodeToMarkdown(HtmlNode node, StringBuilder markdown, int indentLevel)
    {
        if (node == null) return;

        switch (node.NodeType)
        {
            case HtmlNodeType.Text:
                var text = HtmlEntity.DeEntitize(node.InnerText);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Preserve word spacing but normalize multiple whitespace to single spaces
                    var cleanText = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
                    // Only trim if it's all whitespace or newlines
                    if (!string.IsNullOrWhiteSpace(cleanText))
                    {
                        markdown.Append(cleanText);
                    }
                }
                break;

            case HtmlNodeType.Element:
                ConvertElementToMarkdown(node, markdown, indentLevel);
                break;

            case HtmlNodeType.Document:
                // Process all child nodes for document root
                ConvertChildrenToMarkdown(node, markdown, indentLevel);
                break;
        }
    }

    private static void ConvertElementToMarkdown(HtmlNode element, StringBuilder markdown, int indentLevel)
    {
        var tagName = element.Name.ToLowerInvariant();

        switch (tagName)
        {
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                var level = int.Parse(tagName[1].ToString());
                markdown.AppendLine();
                markdown.Append(new string('#', level));
                markdown.Append(' ');
                ConvertChildrenToMarkdown(element, markdown, indentLevel);
                markdown.AppendLine();
                markdown.AppendLine();
                break;

            case "p":
                markdown.AppendLine();
                ConvertChildrenToMarkdown(element, markdown, indentLevel);
                markdown.AppendLine();
                markdown.AppendLine();
                break;

            case "br":
                markdown.AppendLine();
                break;

            case "strong":
            case "b":
                markdown.Append("**");
                ConvertChildrenToMarkdown(element, markdown, indentLevel);
                markdown.Append("**");
                break;

            case "em":
            case "i":
                markdown.Append("*");
                ConvertChildrenToMarkdown(element, markdown, indentLevel);
                markdown.Append("*");
                break;

            case "code":
                markdown.Append("`");
                ConvertChildrenToMarkdown(element, markdown, indentLevel);
                markdown.Append("`");
                break;

            case "pre":
                markdown.AppendLine();
                markdown.AppendLine("```");
                ConvertChildrenToMarkdown(element, markdown, indentLevel);
                markdown.AppendLine();
                markdown.AppendLine("```");
                markdown.AppendLine();
                break;

            case "a":
                var href = element.GetAttributeValue("href", "");
                if (!string.IsNullOrEmpty(href))
                {
                    markdown.Append("[");
                    ConvertChildrenToMarkdown(element, markdown, indentLevel);
                    markdown.Append("](");
                    markdown.Append(href);
                    markdown.Append(")");
                }
                else
                {
                    ConvertChildrenToMarkdown(element, markdown, indentLevel);
                }
                break;

            case "img":
                var src = element.GetAttributeValue("src", "");
                var alt = element.GetAttributeValue("alt", "");
                if (!string.IsNullOrEmpty(src))
                {
                    markdown.Append("![");
                    markdown.Append(alt);
                    markdown.Append("](");
                    markdown.Append(src);
                    markdown.Append(")");
                }
                break;

            case "ul":
            case "ol":
                markdown.AppendLine();
                ConvertChildrenToMarkdown(element, markdown, indentLevel);
                markdown.AppendLine();
                break;

            case "li":
                markdown.Append(new string(' ', indentLevel * 2));
                if (element.ParentNode?.Name.ToLowerInvariant() == "ol")
                {
                    markdown.Append("1. ");
                }
                else
                {
                    markdown.Append("- ");
                }
                ConvertChildrenToMarkdown(element, markdown, indentLevel + 1);
                markdown.AppendLine();
                break;

            case "blockquote":
                markdown.AppendLine();
                markdown.Append("> ");
                ConvertChildrenToMarkdown(element, markdown, indentLevel);
                markdown.AppendLine();
                markdown.AppendLine();
                break;

            case "table":
                markdown.AppendLine();
                ConvertTableToMarkdown(element, markdown);
                markdown.AppendLine();
                break;

            case "hr":
                markdown.AppendLine();
                markdown.AppendLine("---");
                markdown.AppendLine();
                break;

            // Skip script, style, and other non-content elements
            case "script":
            case "style":
            case "meta":
            case "link":
            case "head":
                break;

            default:
                // For other elements, just process children
                ConvertChildrenToMarkdown(element, markdown, indentLevel);
                break;
        }
    }

    private static void ConvertChildrenToMarkdown(HtmlNode element, StringBuilder markdown, int indentLevel)
    {
        if (element?.ChildNodes == null) return;
        
        foreach (var child in element.ChildNodes)
        {
            ConvertNodeToMarkdown(child, markdown, indentLevel);
        }
    }

    private static void ConvertTableToMarkdown(HtmlNode table, StringBuilder markdown)
    {
        var rows = table.SelectNodes(".//tr");
        if (rows is null || rows.Count == 0)
            return;

        var isFirstRow = true;
        foreach (var row in rows)
        {
            var cells = row.SelectNodes(".//td | .//th");
            if (cells is null || cells.Count == 0)
                continue;

            markdown.Append("|");
            foreach (var cell in cells)
            {
                markdown.Append(" ");
                var cellContent = new StringBuilder();
                ConvertChildrenToMarkdown(cell, cellContent, 0);
                markdown.Append(cellContent.ToString().Trim().Replace("|", "\\|"));
                markdown.Append(" |");
            }
            markdown.AppendLine();

            // Add header separator after first row
            if (isFirstRow)
            {
                markdown.Append("|");
                for (int i = 0; i < cells.Count; i++)
                {
                    markdown.Append(" --- |");
                }
                markdown.AppendLine();
                isFirstRow = false;
            }
        }
    }
}