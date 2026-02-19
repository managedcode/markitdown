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
public sealed partial class WikiMarkupConverter : DocumentConverterBase
{
    public WikiMarkupConverter()
        : base(priority: 151)
    {
    }

    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".wiki",
        ".mediawiki",
        ".creole",
        ".dokuwiki",
    };

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        MimeHelper.GetMimeType(".wiki") ?? "text/x-wiki",
        MimeHelper.GetMimeType(".mediawiki") ?? "text/x-mediawiki",
    };

    private static readonly Regex LinkPattern = MyRegex();

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = streamInfo.ResolveMimeType();
        var extension = streamInfo.Extension?.ToLowerInvariant();
        if (extension is not null && Extensions.Contains(extension))
        {
            return true;
        }

        return StreamInfo.MatchesMime(normalizedMime, MimeTypes)
            || StreamInfo.MatchesMime(streamInfo.MimeType, MimeTypes);
    }

    public override async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
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

    [GeneratedRegex(@"\[\[(?<target>[^|\]]+)(\|(?<text>[^\]]+))?\]\]", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}
