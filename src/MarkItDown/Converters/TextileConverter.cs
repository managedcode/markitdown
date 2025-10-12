using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for Textile documents.
/// </summary>
public sealed class TextileConverter : IDocumentConverter
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".textile",
    };

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        MimeHelper.GetMimeType(".textile") ?? MimeTypeUtilities.Compose("text", "textile"),
    };

    private static readonly Regex Heading = new("^h(?<level>[1-6])\\.\\s*(?<text>.+)$", RegexOptions.Compiled);
    private static readonly Regex Bold = new("\\*(?<text>[^*]+)\\*", RegexOptions.Compiled);
    private static readonly Regex Italic = new("_(?<text>[^_]+)_", RegexOptions.Compiled);

    public int Priority => 152;

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
        return new DocumentConverterResult(ConvertToMarkdown(content), streamInfo.FileName);
    }

    private static string ConvertToMarkdown(string textile)
    {
        var lines = textile.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                builder.AppendLine();
                continue;
            }

            var headingMatch = Heading.Match(trimmed);
            if (headingMatch.Success)
            {
                var level = int.Parse(headingMatch.Groups["level"].Value, System.Globalization.CultureInfo.InvariantCulture);
                builder.AppendLine(new string('#', Math.Clamp(level, 1, 6)) + " " + headingMatch.Groups["text"].Value.Trim());
                continue;
            }

            if (trimmed.StartsWith("* "))
            {
                builder.AppendLine("- " + trimmed[2..].Trim());
                continue;
            }

            if (trimmed.StartsWith("# "))
            {
                builder.AppendLine("1. " + trimmed[2..].Trim());
                continue;
            }

            var converted = Bold.Replace(trimmed, m => "**" + m.Groups["text"].Value + "**");
            converted = Italic.Replace(converted, m => "*" + m.Groups["text"].Value + "*");
            builder.AppendLine(converted);
        }

        return builder.ToString().Trim();
    }
}
