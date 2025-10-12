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
/// Converter for Typst documents using heuristic mapping to Markdown.
/// </summary>
public sealed class TypstConverter : IDocumentConverter
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".typ",
        ".typst",
    };

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        MimeHelper.GetMimeType(".typ") ?? MimeTypeUtilities.Compose("text", "typst"),
        MimeHelper.GetMimeType(".typst") ?? MimeTypeUtilities.Compose("text", "typst"),
    };

    public int Priority => 153;

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

    private static string ConvertToMarkdown(string typst)
    {
        var lines = typst.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                builder.AppendLine();
                continue;
            }

            if (trimmed.StartsWith("= "))
            {
                builder.AppendLine("# " + trimmed[2..].Trim());
                continue;
            }

            if (trimmed.StartsWith("=="))
            {
                var level = trimmed.TakeWhile(c => c == '=').Count();
                builder.AppendLine(new string('#', Math.Clamp(level, 1, 6)) + " " + trimmed[level..].Trim());
                continue;
            }

            if (trimmed.StartsWith("#"))
            {
                builder.AppendLine("- " + trimmed[1..].Trim());
                continue;
            }

            builder.AppendLine(trimmed.Replace("#bold", "**").Replace("#italic", "*"));
        }

        return builder.ToString().Trim();
    }
}
