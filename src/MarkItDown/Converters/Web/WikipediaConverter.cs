using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using System.Text.RegularExpressions;

namespace MarkItDown.Converters;

/// <summary>
/// Specialized converter for Wikipedia HTML pages that extracts only the main article content.
/// </summary>
public sealed partial class WikipediaConverter : DocumentConverterBase
{
    public WikipediaConverter()
        : base(priority: 95)
    {
    }

    private static readonly Regex WikipediaHostRegex = MyRegex();

    private static readonly HashSet<string> AcceptedExtensions =
    [
        ".html",
        ".htm",
    ];

    private static readonly HashSet<string> AcceptedMimePrefixes =
    [
        "text/html",
        "application/xhtml",
    ];

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var url = streamInfo.Url ?? string.Empty;
        if (!WikipediaHostRegex.IsMatch(url))
        {
            return false;
        }

        var extension = streamInfo.Extension?.ToLowerInvariant();
        if (extension is not null && AcceptedExtensions.Contains(extension))
        {
            return true;
        }

        var mime = (streamInfo.MimeType ?? string.Empty).ToLowerInvariant();
        return AcceptedMimePrefixes.Any(mime.StartsWith);
    }

    public override async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        var html = await HtmlContentLoader.ReadHtmlAsync(stream, streamInfo, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(html))
        {
            return new DocumentConverterResult(string.Empty, null);
        }

        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(html, cancellationToken).ConfigureAwait(false);

        RemoveNodes(document, "script,style,noscript");

        var renderer = new HtmlMarkdownRenderer();
        var mainContent = document.QuerySelector("div#mw-content-text");
        var title = document.QuerySelector("span.mw-page-title-main")?.TextContent?.Trim();

        string markdown;
        if (mainContent is not null)
        {
            markdown = HtmlMarkdownRenderer.RenderFragment(mainContent);
            if (!string.IsNullOrWhiteSpace(title))
            {
                markdown = $"# {title}\n\n" + markdown;
            }
        }
        else
        {
            var renderResult = HtmlMarkdownRenderer.RenderDocument(document);
            markdown = renderResult.Markdown;
            title ??= renderResult.Title;
        }

        var documentTitle = title ?? document.Title?.Trim();
        return new DocumentConverterResult(markdown.Trim(), documentTitle);
    }

    private static void RemoveNodes(IDocument document, string selector)
    {
        var nodes = document.QuerySelectorAll(selector);
        foreach (var node in nodes)
        {
            node.Remove();
        }
    }

    [GeneratedRegex("^https?://[a-zA-Z]{2,3}\\.wikipedia\\.org/", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}
