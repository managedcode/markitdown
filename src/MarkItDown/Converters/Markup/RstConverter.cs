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
/// Converter for reStructuredText documents.
/// </summary>
public sealed partial class RstConverter : DocumentConverterBase
{
    public RstConverter()
        : base(priority: 165)
    {
    }

    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".rst",
        ".rest",
    };

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        MimeHelper.GetMimeType(".rst") ?? "text/x-rst",
        MimeHelper.GetMimeType(".rest") ?? "text/x-rst",
    };

    private static readonly Regex InlineLiteral = MyRegex();
    private static readonly Regex Bold = MyRegex1();
    private static readonly Regex Italic = MyRegex2();

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

    private static string ConvertToMarkdown(string rst)
    {
        var lines = rst.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length > 0 && i + 1 < lines.Length && IsUnderline(lines[i + 1], line.Length))
            {
                var level = DetermineHeadingLevel(lines[i + 1]);
                builder.AppendLine(new string('#', level) + " " + line.Trim());
                builder.AppendLine();
                i++; // Skip underline
                continue;
            }

            if (line.StartsWith(".. "))
            {
                continue;
            }

            var converted = line;
            converted = InlineLiteral.Replace(converted, m => "`" + m.Groups["text"].Value + "`");
            converted = Bold.Replace(converted, m => "**" + m.Groups["text"].Value + "**");
            converted = Italic.Replace(converted, m => "*" + m.Groups["text"].Value + "*");
            converted = converted.Replace("::", ":");

            builder.AppendLine(converted);
        }

        return builder.ToString().Trim();
    }

    private static bool IsUnderline(string candidate, int length)
    {
        if (string.IsNullOrWhiteSpace(candidate) || length <= 0 || candidate.Length < length)
        {
            return false;
        }

        return candidate.Take(length).All(ch => ch is '=' or '-' or '~' or '^' or '"' or '`' or '.' or '*');
    }

    private static int DetermineHeadingLevel(string underline)
    {
        var ch = underline.Trim().FirstOrDefault();
        return ch switch
        {
            '=' => 1,
            '-' => 2,
            '~' => 3,
            '^' => 4,
            '"' => 5,
            _ => 2,
        };
    }

    [GeneratedRegex("``(?<text>[^`]+)``", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
    [GeneratedRegex("\\*\\*(?<text>[^*]+)\\*\\*", RegexOptions.Compiled)]
    private static partial Regex MyRegex1();
    [GeneratedRegex("\\*(?<text>[^*]+)\\*", RegexOptions.Compiled)]
    private static partial Regex MyRegex2();
}
