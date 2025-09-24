using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using System.Linq;
using System.Text;

namespace MarkItDown.Converters;

internal sealed class HtmlMarkdownRenderer
{
    public HtmlRenderResult RenderDocument(IHtmlDocument document)
    {
        var markdown = new StringBuilder();
        ConvertNode(document.DocumentElement, markdown, 0);
        var normalized = NormalizeSpacing(markdown.ToString());
        var title = ExtractTitle(document);
        return new HtmlRenderResult(normalized, title);
    }

    public string RenderFragment(INode node)
    {
        var markdown = new StringBuilder();
        ConvertNode(node, markdown, 0);
        return NormalizeSpacing(markdown.ToString());
    }

    private static string NormalizeSpacing(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var result = value.Trim();
        while (result.Contains("\n\n\n", StringComparison.Ordinal))
        {
            result = result.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        }

        return result;
    }

    private static string? ExtractTitle(IHtmlDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.Title))
        {
            return document.Title.Trim();
        }

        var heading = document.QuerySelector("h1")?.TextContent;
        if (!string.IsNullOrWhiteSpace(heading))
        {
            return heading.Trim();
        }

        return null;
    }

    private static void ConvertNode(INode? node, StringBuilder markdown, int indentLevel)
    {
        if (node is null)
        {
            return;
        }

        switch (node.NodeType)
        {
            case NodeType.Document:
                foreach (var child in node.ChildNodes)
                {
                    ConvertNode(child, markdown, indentLevel);
                }
                break;

            case NodeType.Text:
                AppendText(node, markdown);
                break;

            case NodeType.Element:
                ConvertElement((IElement)node, markdown, indentLevel);
                break;
        }
    }

    private static void AppendText(INode node, StringBuilder markdown)
    {
        var text = node.TextContent;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var cleanText = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        markdown.Append(cleanText);
    }

    private static void ConvertElement(IElement element, StringBuilder markdown, int indentLevel)
    {
        var tag = element.TagName.ToLowerInvariant();

        switch (tag)
        {
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                var level = int.Parse(tag[1].ToString());
                markdown.AppendLine().Append(new string('#', level)).Append(' ');
                ConvertChildren(element, markdown, indentLevel);
                markdown.AppendLine().AppendLine();
                break;

            case "p":
            case "div":
            case "section":
            case "article":
                markdown.AppendLine();
                ConvertChildren(element, markdown, indentLevel);
                markdown.AppendLine().AppendLine();
                break;

            case "blockquote":
            {
                var inner = new StringBuilder();
                ConvertChildren(element, inner, indentLevel);
                var normalized = NormalizeSpacing(inner.ToString());
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    foreach (var line in normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        markdown.AppendLine($"> {line}");
                    }
                    markdown.AppendLine();
                }
                break;
            }

            case "br":
                markdown.AppendLine();
                break;

            case "strong":
            case "b":
                markdown.Append("**");
                ConvertChildren(element, markdown, indentLevel);
                markdown.Append("**");
                break;

            case "em":
            case "i":
                markdown.Append('*');
                ConvertChildren(element, markdown, indentLevel);
                markdown.Append('*');
                break;

            case "code":
                markdown.Append('`');
                ConvertChildren(element, markdown, indentLevel);
                markdown.Append('`');
                break;

            case "pre":
                markdown.AppendLine("```");
                ConvertChildren(element, markdown, indentLevel);
                markdown.AppendLine().AppendLine("```");
                break;

            case "ul":
                markdown.AppendLine();
                ConvertChildren(element, markdown, indentLevel + 1);
                markdown.AppendLine();
                break;

            case "ol":
                markdown.AppendLine();
                var index = 1;
                foreach (var child in element.Children)
                {
                    if (!string.Equals(child.TagName, "li", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    markdown.Append(new string(' ', indentLevel * 2));
                    markdown.Append(index++).Append('.').Append(' ');
                    ConvertChildren(child, markdown, indentLevel + 1);
                    markdown.AppendLine();
                }

                markdown.AppendLine();
                break;

            case "li":
                markdown.Append(new string(' ', Math.Max(indentLevel - 1, 0) * 2));
                markdown.Append("- ");
                ConvertChildren(element, markdown, indentLevel + 1);
                markdown.AppendLine();
                break;

            case "a":
                var href = element.GetAttribute("href") ?? string.Empty;
                var linkText = new StringBuilder();
                ConvertChildren(element, linkText, indentLevel);
                markdown.Append('[').Append(linkText.ToString().Trim()).Append("](").Append(href).Append(')');
                break;

            case "img":
                var alt = element.GetAttribute("alt") ?? string.Empty;
                var src = element.GetAttribute("src") ?? string.Empty;
                markdown.Append("![").Append(alt).Append("](").Append(src).Append(')');
                break;

            case "table":
                RenderTable(element, markdown);
                break;

            case "thead":
            case "tbody":
            case "tfoot":
            case "tr":
                ConvertChildren(element, markdown, indentLevel);
                markdown.AppendLine();
                break;

            default:
                ConvertChildren(element, markdown, indentLevel);
                break;
        }
    }

    private static void ConvertChildren(INode node, StringBuilder markdown, int indentLevel)
    {
        foreach (var child in node.ChildNodes)
        {
            ConvertNode(child, markdown, indentLevel);
        }
    }

    private static void RenderTable(IElement table, StringBuilder markdown)
    {
        var rows = table.QuerySelectorAll("tr").OfType<IElement>().ToList();
        if (rows.Count == 0)
        {
            return;
        }

        static string CellText(IElement cell)
        {
            var text = cell.TextContent ?? string.Empty;
            return text.Trim();
        }

        var headerRow = new List<string>();
        var dataRows = new List<List<string>>();

        foreach (var row in rows)
        {
            var cells = row.QuerySelectorAll("th,td").OfType<IElement>().Select(CellText).ToList();
            if (cells.Count == 0)
            {
                continue;
            }

            if (headerRow.Count == 0 && row.QuerySelector("th") is not null)
            {
                headerRow = cells;
            }
            else
            {
                dataRows.Add(cells);
            }
        }

        if (headerRow.Count == 0)
        {
            headerRow = dataRows.FirstOrDefault() ?? new List<string>();
            if (dataRows.Count > 0)
            {
                dataRows.RemoveAt(0);
            }
        }

        if (headerRow.Count == 0)
        {
            return;
        }

        string FormatRow(IReadOnlyList<string> cells)
        {
            var values = headerRow
                .Select((_, index) => index < cells.Count ? cells[index] : string.Empty)
                .ToList();

            return "| " + string.Join(" | ", values) + " |";
        }

        var separator = "| " + string.Join(" | ", headerRow.Select(_ => "---")) + " |";

        markdown.AppendLine();
        markdown.AppendLine(FormatRow(headerRow));
        markdown.AppendLine(separator);

        foreach (var row in dataRows)
        {
            markdown.AppendLine(FormatRow(row));
        }

        markdown.AppendLine();
    }
}

internal readonly record struct HtmlRenderResult(string Markdown, string? Title);
