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
/// Converter for generic wiki markup (MediaWiki style).
/// </summary>
public sealed class WikiMarkupConverter : IDocumentConverter
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".wiki",
        ".mediawiki",
        ".creole",
        ".dokuwiki",
    };

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        MimeHelper.GetMimeType(".wiki") ?? MimeTypeUtilities.Compose("text", "x-wiki"),
        MimeHelper.GetMimeType(".mediawiki") ?? MimeTypeUtilities.Compose("text", "x-mediawiki"),
    };

    private static readonly Regex LinkPattern = new(@"\[\[(?<target>[^|\]]+)(\|(?<text>[^\]]+))?\]\]", RegexOptions.Compiled);

    public int Priority => 151;

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

    private static string ConvertToMarkdown(string wiki)
    {
        var lines = wiki.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                builder.AppendLine();
                continue;
            }

            if (trimmed.StartsWith("="))
            {
                var level = trimmed.TakeWhile(c => c == '=').Count();
                var text = trimmed.Trim('=').Trim();
                builder.AppendLine(new string('#', Math.Clamp(level, 1, 6)) + " " + text);
                continue;
            }

            if (trimmed.StartsWith("*"))
            {
                builder.AppendLine("- " + trimmed.TrimStart('*').Trim());
                continue;
            }

            if (trimmed.StartsWith("#"))
            {
                builder.AppendLine("1. " + trimmed.TrimStart('#').Trim());
                continue;
            }

            var converted = LinkPattern.Replace(trimmed, m =>
            {
                var target = m.Groups["target"].Value.Trim();
                var text = m.Groups["text"].Success ? m.Groups["text"].Value.Trim() : target;
                return $"[{text}]({target})";
            });

            builder.AppendLine(converted.Replace("'''''", "**").Replace("'''", "**").Replace("''", "*"));
        }

        return builder.ToString().Trim();
    }
}
