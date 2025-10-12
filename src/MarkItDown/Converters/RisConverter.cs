using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for RIS bibliographic records.
/// </summary>
public sealed class RisConverter : IDocumentConverter
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".ris",
    };

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        MimeHelper.GetMimeType(".ris") ?? MimeTypeUtilities.Compose("application", "ris"),
    };

    public int Priority => 139;

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

    private static string RenderMarkdown(string ris)
    {
        var entries = ParseEntries(ris);
        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            var title = entry.TryGetValue("TI", out var ti) ? ti.FirstOrDefault() : entry.TryGetValue("T1", out var t1) ? t1.FirstOrDefault() : "Untitled";
            var authors = entry.TryGetValue("AU", out var au)
                ? (IReadOnlyList<string>)au
                : Array.Empty<string>();
            var year = entry.TryGetValue("PY", out var py) ? py.FirstOrDefault() : null;
            var journal = entry.TryGetValue("JO", out var jo) ? jo.FirstOrDefault() : entry.TryGetValue("JF", out var jf) ? jf.FirstOrDefault() : null;
            var url = entry.TryGetValue("UR", out var ur) ? ur.FirstOrDefault() : null;

            builder.Append("- **");
            builder.Append(title?.Trim());
            builder.Append("**");

            if (authors.Count > 0)
            {
                builder.Append(" — ");
                builder.Append(string.Join(", ", authors.Select(a => a.Trim())));
            }

            if (!string.IsNullOrWhiteSpace(journal))
            {
                builder.Append(", ");
                builder.Append(journal?.Trim());
            }

            if (!string.IsNullOrWhiteSpace(year))
            {
                builder.Append(" (" + year?.Trim() + ")");
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                builder.Append($" [link]({url})");
            }

            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static List<Dictionary<string, List<string>>> ParseEntries(string ris)
    {
        var entries = new List<Dictionary<string, List<string>>>();
        Dictionary<string, List<string>>? current = null;

        using var reader = new StringReader(ris);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length < 6 || !line.Contains(" - "))
            {
                continue;
            }

            var tag = line[..2];
            var value = line.Length > 6 ? line[6..].Trim() : string.Empty;

            if (tag.Equals("TY", StringComparison.OrdinalIgnoreCase))
            {
                current = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                entries.Add(current);
                AddValue(current, tag, value);
            }
            else if (tag.Equals("ER", StringComparison.OrdinalIgnoreCase))
            {
                current = null;
            }
            else if (current is not null)
            {
                AddValue(current, tag, value);
            }
        }

        return entries;
    }

    private static void AddValue(Dictionary<string, List<string>> entry, string tag, string value)
    {
        if (!entry.TryGetValue(tag, out var list))
        {
            list = new List<string>();
            entry[tag] = list;
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            list.Add(value);
        }
    }
}
