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
/// Converter for AsciiDoc documents.
/// </summary>
public sealed partial class AsciiDocConverter : DocumentConverterBase
{
    public AsciiDocConverter()
        : base(priority: 160)
    {
    }

    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".adoc",
        ".asciidoc",
    };

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        MimeHelper.GetMimeType(".adoc") ?? "text/asciidoc",
        MimeHelper.GetMimeType(".asciidoc") ?? "text/asciidoc",
    };

    private static readonly Regex Bold = MyRegex();
    private static readonly Regex Italic = MyRegex1();
    private static readonly Regex Monospace = MyRegex2();

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
        var markdown = ConvertToMarkdown(content);
        return new DocumentConverterResult(markdown, streamInfo.FileName);
    }

    private static string ConvertToMarkdown(string adoc)
    {
        var lines = adoc.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                builder.AppendLine();
                continue;
            }

            if (trimmed.StartsWith("= "))
            {
                var level = trimmed.TakeWhile(c => c == '=').Count();
                builder.AppendLine(new string('#', Math.Clamp(level, 1, 6)) + " " + trimmed[level..].Trim());
                continue;
            }

            if (trimmed.StartsWith("=="))
            {
                var level = trimmed.TakeWhile(c => c == '=').Count();
                builder.AppendLine(new string('#', Math.Clamp(level, 1, 6)) + " " + trimmed[level..].Trim());
                continue;
            }

            if (trimmed.StartsWith("*") || trimmed.StartsWith("-") || trimmed.StartsWith("."))
            {
                var marker = trimmed[0] == '.' ? "1." : "-";
                builder.AppendLine(marker + " " + trimmed[1..].Trim());
                continue;
            }

            var converted = Bold.Replace(trimmed, m => "**" + m.Groups["text"].Value + "**");
            converted = Italic.Replace(converted, m => "*" + m.Groups["text"].Value + "*");
            converted = Monospace.Replace(converted, m => "`" + m.Groups["text"].Value + "`");

            builder.AppendLine(converted);
        }

        return builder.ToString().Trim();
    }

    [GeneratedRegex("\\*(?<text>[^*]+)\\*", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
    [GeneratedRegex("_(?<text>[^_]+)_", RegexOptions.Compiled)]
    private static partial Regex MyRegex1();
    [GeneratedRegex("`(?<text>[^`]+)`", RegexOptions.Compiled)]
    private static partial Regex MyRegex2();
}
