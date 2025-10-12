using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for CSL JSON bibliographies.
/// </summary>
public sealed class CslJsonConverter : IDocumentConverter
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".csljson",
    };

    private static readonly string DefaultCslMime = MimeHelper.GetMimeType(".csljson") ?? string.Empty;

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        string.IsNullOrWhiteSpace(DefaultCslMime) || DefaultCslMime.StartsWith(MimeHelper.JSON, StringComparison.OrdinalIgnoreCase)
            ? MimeTypeUtilities.Compose(MimeHelper.APPLICATION, "vnd.citationstyles.csl+json")
            : DefaultCslMime,
        MimeTypeUtilities.Compose(MimeHelper.APPLICATION, "vnd.citationstyles.csl+json"),
        MimeTypeUtilities.Compose(MimeHelper.APPLICATION, "citeproc+json"),
    };

    public int Priority => 138;

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

    private static string RenderMarkdown(string json)
    {
        using var document = JsonDocument.Parse(json);
        var builder = new StringBuilder();

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in document.RootElement.EnumerateArray())
            {
                builder.AppendLine(RenderEntry(element));
            }
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            builder.AppendLine(RenderEntry(document.RootElement));
        }

        return builder.ToString().Trim();
    }

    private static string RenderEntry(JsonElement element)
    {
        var title = element.TryGetProperty("title", out var titleProperty)
            ? titleProperty.GetString()
            : element.TryGetProperty("id", out var idProperty) ? idProperty.GetString() : "Untitled";
        var year = element.TryGetProperty("issued", out var issued)
            && issued.TryGetProperty("date-parts", out var dateParts)
            && dateParts.ValueKind == JsonValueKind.Array
            && dateParts.GetArrayLength() > 0
            && dateParts[0].ValueKind == JsonValueKind.Array
            && dateParts[0].GetArrayLength() > 0
                ? dateParts[0][0].GetRawText()
                : null;
        var container = element.TryGetProperty("container-title", out var containerTitle)
            ? containerTitle.GetString()
            : null;
        var url = element.TryGetProperty("URL", out var urlProperty)
            ? urlProperty.GetString()
            : element.TryGetProperty("DOI", out var doiProperty) ? "https://doi.org/" + doiProperty.GetString() : null;

        var authors = new List<string>();
        if (element.TryGetProperty("author", out var authorArray) && authorArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var author in authorArray.EnumerateArray())
            {
                var parts = new List<string>();
                if (author.TryGetProperty("given", out var given))
                {
                    parts.Add(given.GetString() ?? string.Empty);
                }

                if (author.TryGetProperty("family", out var family))
                {
                    parts.Add(family.GetString() ?? string.Empty);
                }

                var formatted = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
                if (!string.IsNullOrWhiteSpace(formatted))
                {
                    authors.Add(formatted);
                }
            }
        }

        var lineBuilder = new StringBuilder();
        lineBuilder.Append("- **");
        lineBuilder.Append(title);
        lineBuilder.Append("**");

        if (authors.Count > 0)
        {
            lineBuilder.Append(" — ");
            lineBuilder.Append(string.Join(", ", authors));
        }

        if (!string.IsNullOrWhiteSpace(container))
        {
            lineBuilder.Append(", ");
            lineBuilder.Append(container);
        }

        if (!string.IsNullOrWhiteSpace(year))
        {
            lineBuilder.Append(" (" + year + ")");
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            lineBuilder.Append($" [link]({url})");
        }

        return lineBuilder.ToString();
    }
}
