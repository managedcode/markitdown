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
/// Simplistic converter for LaTeX documents.
/// </summary>
public sealed partial class LatexConverter : DocumentConverterBase
{
    public LatexConverter()
        : base(priority: 170)
    {
    }

    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".tex",
        ".latex",
    };

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        MimeHelper.GetMimeType(".tex") ?? "text/x-tex",
        MimeHelper.GetMimeType(".latex") ?? "text/x-latex",
        "application/x-tex",
    };

    private static readonly Regex SectionRegex = MyRegex();
    private static readonly Regex BoldRegex = MyRegex1();
    private static readonly Regex EmphRegex = MyRegex2();
    private static readonly Regex TitleRegex = MyRegex3();

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
        var titleMatch = TitleRegex.Match(content);
        var title = titleMatch.Success ? titleMatch.Groups["title"].Value.Trim() : streamInfo.FileName;
        return new DocumentConverterResult(markdown, title);
    }

    private static string ConvertToMarkdown(string latex)
    {
        var normalized = latex.Replace("\r\n", "\n");
        normalized = TitleRegex.Replace(normalized, string.Empty);
        normalized = SectionRegex.Replace(normalized, match =>
        {
            var levelToken = match.Groups["level"].Value;
            var level = string.IsNullOrEmpty(levelToken) ? 1 : levelToken.Length / 3 + 2;
            level = Math.Clamp(level, 1, 6);
            return "\n" + new string('#', level) + " " + match.Groups["title"].Value.Trim() + "\n";
        });

        normalized = MyRegex4().Replace(normalized, m => "\n#### " + m.Groups[1].Value.Trim() + "\n");
        normalized = BoldRegex.Replace(normalized, m => "**" + m.Groups["text"].Value + "**");
        normalized = EmphRegex.Replace(normalized, m => "*" + m.Groups["text"].Value + "*");
        normalized = MyRegex5().Replace(normalized, "\n");
        normalized = MyRegex6().Replace(normalized, "\n");
        normalized = MyRegex7().Replace(normalized, "\n");
        normalized = MyRegex8().Replace(normalized, "\n");
        normalized = MyRegex9().Replace(normalized, "\n- ");
        normalized = MyRegex10().Replace(normalized, "\n");
        normalized = MyRegex11().Replace(normalized, string.Empty);
        normalized = MyRegex12().Replace(normalized, string.Empty);
        normalized = MyRegex13().Replace(normalized, string.Empty);

        var lines = normalized.Split('\n');
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                builder.AppendLine();
            }
            else
            {
                builder.AppendLine(trimmed);
            }
        }

        return builder.ToString().Trim();
    }

    [GeneratedRegex(@"\\(?<level>sub)*section\{(?<title>[^}]*)\}", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
    [GeneratedRegex(@"\\textbf\{(?<text>[^}]*)\}", RegexOptions.Compiled)]
    private static partial Regex MyRegex1();
    [GeneratedRegex(@"\\emph\{(?<text>[^}]*)\}", RegexOptions.Compiled)]
    private static partial Regex MyRegex2();
    [GeneratedRegex(@"\\title\{(?<title>[^}]*)\}", RegexOptions.Compiled)]
    private static partial Regex MyRegex3();
    [GeneratedRegex(@"\\subsubsection\{([^}]*)\}")]
    private static partial Regex MyRegex4();
    [GeneratedRegex(@"\\begin\{itemize\}")]
    private static partial Regex MyRegex5();
    [GeneratedRegex(@"\\end\{itemize\}")]
    private static partial Regex MyRegex6();
    [GeneratedRegex(@"\\begin\{enumerate\}")]
    private static partial Regex MyRegex7();
    [GeneratedRegex(@"\\end\{enumerate\}")]
    private static partial Regex MyRegex8();
    [GeneratedRegex(@"\\item\s+")]
    private static partial Regex MyRegex9();
    [GeneratedRegex(@"\\\\")]
    private static partial Regex MyRegex10();
    [GeneratedRegex(@"\\\[[^\]]*\\\]")]
    private static partial Regex MyRegex11();
    [GeneratedRegex(@"\\cite\{[^}]*\}")]
    private static partial Regex MyRegex12();
    [GeneratedRegex(@"\\[a-zA-Z]+")]
    private static partial Regex MyRegex13();
}
