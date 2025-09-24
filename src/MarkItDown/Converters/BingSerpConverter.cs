using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using System.Text;
using System.Text.RegularExpressions;

namespace MarkItDown.Converters;

/// <summary>
/// Extracts organic results from Bing search result pages.
/// </summary>
public sealed class BingSerpConverter : IDocumentConverter
{
    private static readonly Regex BingQueryRegex = new("^https://www\\.bing\\.com/search\\?q=", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

    public int Priority => 96;

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        => AcceptsInput(streamInfo);

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var url = streamInfo.Url ?? string.Empty;
        if (!BingQueryRegex.IsMatch(url))
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

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        var html = await HtmlContentLoader.ReadHtmlAsync(stream, streamInfo, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(html))
        {
            return new DocumentConverterResult(string.Empty, null);
        }

        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(html, cancellationToken).ConfigureAwait(false);

        // Clean up specific Bing formatting quirks
        foreach (var element in document.QuerySelectorAll(".tptt"))
        {
            if (!string.IsNullOrWhiteSpace(element.TextContent))
            {
                element.TextContent += " ";
            }
        }

        foreach (var slug in document.QuerySelectorAll(".algoSlug_icon"))
        {
            slug.Remove();
        }

        var renderer = new HtmlMarkdownRenderer();
        var results = new List<string>();

        foreach (var resultElement in document.QuerySelectorAll(".b_algo"))
        {
            RewriteRedirectLinks(resultElement);

            var fragmentMarkdown = renderer.RenderFragment(resultElement);
            var normalized = NormalizeLines(fragmentMarkdown);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                results.Add(normalized);
            }
        }

        var query = ExtractQuery(streamInfo.Url);
        var markdown = results.Count == 0
            ? $"## No organic results found for '{query}'."
            : $"## A Bing search for '{query}' found the following results:\n\n" + string.Join("\n\n", results);

        var title = document.Title?.Trim();
        return new DocumentConverterResult(markdown.Trim(), title);
    }

    private static string NormalizeLines(string markdown)
    {
        var lines = markdown
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));

        return string.Join("\n", lines);
    }

    private static void RewriteRedirectLinks(IElement resultElement)
    {
        foreach (var anchor in resultElement.QuerySelectorAll("a"))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            if (!Uri.TryCreate(href, UriKind.RelativeOrAbsolute, out var uri))
            {
                continue;
            }

            if (!uri.IsAbsoluteUri)
            {
                continue;
            }

            var query = ParseQuery(uri.Query);
            if (!query.TryGetValue("u", out var encodedValues))
            {
                continue;
            }

            var encoded = encodedValues.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(encoded) || encoded.Length <= 2)
            {
                continue;
            }

            var trimmed = encoded.Substring(2).Trim();
            var decoded = TryDecodeBase64Url(trimmed);
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                anchor.SetAttribute("href", decoded);
            }
        }
    }

    private static string ExtractQuery(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var query = ParseQuery(uri.Query);
        if (query.TryGetValue("q", out var values))
        {
            return values.FirstOrDefault() ?? string.Empty;
        }

        return string.Empty;
    }

    private static Dictionary<string, List<string>> ParseQuery(string query)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var trimmed = query.StartsWith('?') ? query[1..] : query;
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kvp = part.Split('=', 2);
            var key = Uri.UnescapeDataString(kvp[0]);
            var value = kvp.Length > 1 ? Uri.UnescapeDataString(kvp[1]) : string.Empty;

            if (!result.TryGetValue(key, out var list))
            {
                list = [];
                result[key] = list;
            }

            list.Add(value);
        }

        return result;
    }

    private static string? TryDecodeBase64Url(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');

        try
        {
            var bytes = Convert.FromBase64String(padded);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }
}
