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
public sealed class LatexConverter : IDocumentConverter
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".tex",
        ".latex",
    };

    private static readonly IReadOnlyCollection<string> MimeTypes = new[]
    {
        MimeHelper.GetMimeType(".tex") ?? MimeTypeUtilities.Compose("text", "x-tex"),
        MimeHelper.GetMimeType(".latex") ?? MimeTypeUtilities.Compose("text", "x-latex"),
        MimeTypeUtilities.WithType(MimeHelper.XML, "x-tex"),
    };

    private static readonly Regex SectionRegex = new(@"\\(?<level>sub)*section\{(?<title>[^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex BoldRegex = new(@"\\textbf\{(?<text>[^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex EmphRegex = new(@"\\emph\{(?<text>[^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex TitleRegex = new(@"\\title\{(?<title>[^}]*)\}", RegexOptions.Compiled);

    public int Priority => 170;

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

        normalized = Regex.Replace(normalized, @"\\subsubsection\{([^}]*)\}", m => "\n#### " + m.Groups[1].Value.Trim() + "\n");
        normalized = BoldRegex.Replace(normalized, m => "**" + m.Groups["text"].Value + "**");
        normalized = EmphRegex.Replace(normalized, m => "*" + m.Groups["text"].Value + "*");
        normalized = Regex.Replace(normalized, @"\\begin\{itemize\}", "\n");
        normalized = Regex.Replace(normalized, @"\\end\{itemize\}", "\n");
        normalized = Regex.Replace(normalized, @"\\begin\{enumerate\}", "\n");
        normalized = Regex.Replace(normalized, @"\\end\{enumerate\}", "\n");
        normalized = Regex.Replace(normalized, @"\\item\s+", "\n- ");
        normalized = Regex.Replace(normalized, @"\\\\", "\n");
        normalized = Regex.Replace(normalized, @"\\\[[^\]]*\\\]", string.Empty);
        normalized = Regex.Replace(normalized, @"\\cite\{[^}]*\}", string.Empty);
        normalized = Regex.Replace(normalized, @"\\[a-zA-Z]+", string.Empty);

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
}
