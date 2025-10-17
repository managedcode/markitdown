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
/// Converter for Org mode documents.
/// </summary>
public sealed class OrgConverter : DocumentConverterBase
{
    public OrgConverter()
        : base(priority: 155)
    {
    }

    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".org",
    };

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        MimeHelper.GetMimeType(".org") ?? "text/x-org",
    };

    private static readonly Regex Bold = new("\\*(?<text>[^*]+)\\*", RegexOptions.Compiled);
    private static readonly Regex Italic = new("/(?<text>[^/]+)/", RegexOptions.Compiled);
    private static readonly Regex Code = new("=`(?<text>[^`]+)`=", RegexOptions.Compiled);

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

    private static string ConvertToMarkdown(string org)
    {
        var lines = org.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                builder.AppendLine();
                continue;
            }

            if (trimmed.StartsWith("*"))
            {
                var level = trimmed.TakeWhile(c => c == '*').Count();
                builder.AppendLine(new string('#', Math.Clamp(level, 1, 6)) + " " + trimmed[level..].Trim());
                continue;
            }

            if (trimmed.StartsWith("- ") || trimmed.StartsWith("+ "))
            {
                builder.AppendLine("- " + trimmed[2..].Trim());
                continue;
            }

            if (trimmed.StartsWith("1. "))
            {
                builder.AppendLine("1. " + trimmed[3..].Trim());
                continue;
            }

            var converted = Bold.Replace(trimmed, m => "**" + m.Groups["text"].Value + "**");
            converted = Italic.Replace(converted, m => "*" + m.Groups["text"].Value + "*");
            converted = Code.Replace(converted, m => "`" + m.Groups["text"].Value + "`");
            builder.AppendLine(converted);
        }

        return builder.ToString().Trim();
    }
}
