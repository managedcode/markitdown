using AngleSharp.Html.Parser;
using System.Text;

namespace MarkItDown.Converters;

/// <summary>
/// Converts HTML content to Markdown using a lightweight DOM walker built on AngleSharp.
/// </summary>
public sealed class HtmlConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions =
    [
        ".html",
        ".htm",
        ".xhtml",
    ];

    private static readonly HashSet<string> AcceptedMimeTypes =
    [
        "text/html",
        "application/xhtml+xml",
    ];

    public int Priority => 100;

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        => AcceptsInput(streamInfo);

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var mimeType = streamInfo.MimeType?.ToLowerInvariant();
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && AcceptedExtensions.Contains(extension))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(mimeType) && AcceptedMimeTypes.Contains(mimeType))
        {
            return true;
        }

        return false;
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        var html = await HtmlContentLoader.ReadHtmlAsync(stream, streamInfo, cancellationToken);
        if (string.IsNullOrWhiteSpace(html))
        {
            return new DocumentConverterResult(string.Empty, default);
        }

        var parser = new HtmlParser(new HtmlParserOptions
        {
            IsKeepingSourceReferences = false,
            IsEmbedded = false,
        });

        var document = await parser.ParseDocumentAsync(html, cancellationToken).ConfigureAwait(false);
        var renderer = new HtmlMarkdownRenderer();
        var result = renderer.RenderDocument(document);

        return new DocumentConverterResult(result.Markdown, result.Title);
    }

}
