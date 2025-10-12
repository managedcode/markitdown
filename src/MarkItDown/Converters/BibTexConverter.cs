using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for BibTeX bibliographies.
/// </summary>
public sealed class BibTexConverter : IDocumentConverter
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".bib",
        ".bibtex",
    };

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        MimeHelper.GetMimeType(".bib") ?? MimeTypeUtilities.Compose("text", "x-bibtex"),
        MimeHelper.GetMimeType(".bibtex") ?? MimeTypeUtilities.Compose("text", "x-bibtex"),
    };

    private static readonly Regex EntryRegex = new("@(?<type>\\w+)\\s*\\{\\s*(?<key>[^,]+),(?<fields>.*?)\\}\\s*(?=@|\\z)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex FieldRegex = new("(?<name>[A-Za-z]+)\\s*=\\s*(\\{(?<value>[^{}]*)\\}|\"(?<value>[^\"]*)\"|(?<bare>[^,]+))", RegexOptions.Singleline | RegexOptions.Compiled);

    public int Priority => 140;

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = MimeTypeUtilities.NormalizeMime(streamInfo);
        var extension = streamInfo.Extension?.ToLowerInvariant();
        if (extension is not null && Extensions.Contains(extension))
        {
            return true;
        }

        return MimeTypeUtilities.MatchesAny(normalizedMime, MimeTypes)
            || MimeTypeUtilities.MatchesAny(streamInfo.MimeType, MimeTypes);
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default) => AcceptsInput(streamInfo);

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return new DocumentConverterResult(RenderMarkdown(content), streamInfo.FileName);
    }

    private static string RenderMarkdown(string bibtex)
    {
        var builder = new StringBuilder();
        foreach (Match match in EntryRegex.Matches(bibtex))
        {
            var type = match.Groups["type"].Value;
            var key = match.Groups["key"].Value;
            var fields = ParseFields(match.Groups["fields"].Value);
            var title = fields.TryGetValue("title", out var rawTitle) ? rawTitle : key;
            var authors = fields.TryGetValue("author", out var rawAuthors) ? FormatAuthors(rawAuthors) : null;
            var year = fields.TryGetValue("year", out var rawYear) ? rawYear : null;
            var venue = fields.TryGetValue("journal", out var rawJournal) ? rawJournal : fields.TryGetValue("booktitle", out var rawBookTitle) ? rawBookTitle : null;
            var url = fields.TryGetValue("url", out var rawUrl) ? rawUrl : null;

            var lineBuilder = new StringBuilder();
            lineBuilder.Append("- **");
            lineBuilder.Append(title.Trim('{', '}', '"'));
            lineBuilder.Append("**");

            if (!string.IsNullOrEmpty(authors))
            {
                lineBuilder.Append(" — ");
                lineBuilder.Append(authors);
            }

            if (!string.IsNullOrEmpty(venue))
            {
                lineBuilder.Append(", ");
                lineBuilder.Append(venue);
            }

            if (!string.IsNullOrEmpty(year))
            {
                lineBuilder.Append(" (" + year + ")");
            }

            if (!string.IsNullOrEmpty(url))
            {
                lineBuilder.Append($" [link]({url})");
            }

            lineBuilder.Append(" — ``");
            lineBuilder.Append(type);
            lineBuilder.Append("``");

            builder.AppendLine(lineBuilder.ToString());
        }

        return builder.ToString().Trim();
    }

    private static Dictionary<string, string> ParseFields(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match field in FieldRegex.Matches(body))
        {
            var name = field.Groups["name"].Value;
            var value = field.Groups["value"].Success ? field.Groups["value"].Value : field.Groups["bare"].Value;
            if (!string.IsNullOrWhiteSpace(name))
            {
                result[name.Trim()] = value.Trim();
            }
        }

        return result;
    }

    private static string FormatAuthors(string rawAuthors)
    {
        var authors = rawAuthors.Split(new[] { " and " }, StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Trim('{', '}', ' ', '\t', '\n', '\r'))
            .ToArray();
        return string.Join(", ", authors);
    }
}
