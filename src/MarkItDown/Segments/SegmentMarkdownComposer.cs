using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MarkItDown;

internal readonly record struct MetaMarkdown(string Markdown, string? Title);

internal static class SegmentMarkdownComposer
{
    public static MetaMarkdown Compose(
        IReadOnlyList<DocumentSegment> segments,
        ConversionArtifacts? artifacts,
        StreamInfo streamInfo,
        SegmentOptions? options,
        string? titleHint = null,
        DateTime? generatedAtUtc = null)
    {
        var effectiveArtifacts = artifacts ?? ConversionArtifacts.Empty;
        var effectiveOptions = options ?? SegmentOptions.Default;
        var generatedTimestamp = generatedAtUtc ?? DateTime.UtcNow;

        var finalTitle = NormalizeTitle(titleHint)
            ?? ExtractTitleFromSegments(segments)
            ?? GuessTitleFromStreamInfo(streamInfo);

        var builder = new StringBuilder();
        AppendFrontMatter(builder, finalTitle, streamInfo, effectiveArtifacts, segments, generatedTimestamp);
        AppendSegments(builder, segments, effectiveOptions);
        AppendDocumentMetadata(builder, effectiveArtifacts.Metadata);

        var markdown = builder.ToString().TrimEnd();
        return new MetaMarkdown(markdown, finalTitle);
    }

    private static void AppendFrontMatter(
        StringBuilder builder,
        string? title,
        StreamInfo streamInfo,
        ConversionArtifacts artifacts,
        IReadOnlyList<DocumentSegment> segments,
        DateTime generatedAtUtc)
    {
        builder.AppendLine("---");
        AppendYaml(builder, "title", title);

        var source = streamInfo.Url ?? streamInfo.LocalPath ?? streamInfo.FileName;
        AppendYaml(builder, "source", source);

        var mime = streamInfo.ResolveMimeType() ?? streamInfo.MimeType;
        AppendYaml(builder, "mimeType", mime);

        var fileName = streamInfo.FileName;
        AppendYaml(builder, "fileName", fileName);

        AppendYaml(builder, "generated", generatedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        var pageCount = segmentsCount(SegmentType.Page);
        if (pageCount > 0)
        {
            AppendYaml(builder, "pages", pageCount.ToString(CultureInfo.InvariantCulture));
        }

        if (artifacts.Images.Count > 0)
        {
            AppendYaml(builder, "images", artifacts.Images.Count.ToString(CultureInfo.InvariantCulture));
        }

        if (artifacts.Tables.Count > 0)
        {
            AppendYaml(builder, "tables", artifacts.Tables.Count.ToString(CultureInfo.InvariantCulture));
        }

        builder.AppendLine("---");
        builder.AppendLine();

        int segmentsCount(SegmentType type)
            => segments?.Count(segment => segment?.Type == type) ?? 0;
    }

    private static void AppendYaml(StringBuilder builder, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append(key);
        builder.Append(": ");
        builder.AppendLine(EscapeYaml(value));
    }

    private static string EscapeYaml(string value)
    {
        var sanitized = value.Replace("\"", "\\\"", StringComparison.Ordinal);
        sanitized = sanitized.ReplaceLineEndings(" ");
        return $"\"{sanitized.Trim()}\"";
    }

    private static void AppendSegments(StringBuilder builder, IReadOnlyList<DocumentSegment> segments, SegmentOptions options)
    {
        if (segments is null || segments.Count == 0)
        {
            return;
        }

        var first = true;
        foreach (var segment in segments)
        {
            if (segment is null)
            {
                continue;
            }

            var content = TextSanitizer.Normalize(segment.Markdown, trim: true);
            if (string.IsNullOrEmpty(content))
            {
                continue;
            }

            if (!first)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            if (options.IncludeSegmentMetadataInMarkdown)
            {
                var annotation = BuildAnnotation(segment);
                if (!string.IsNullOrEmpty(annotation))
                {
                    builder.AppendLine(annotation);
                }
            }

            builder.Append(content);
            first = false;
        }
    }

    private static void AppendDocumentMetadata(StringBuilder builder, IDictionary<string, string> metadata)
    {
        var comment = MetaMarkdownFormatter.BuildDocumentMetadataComment(metadata);
        if (string.IsNullOrWhiteSpace(comment))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine();
        }

        builder.Append(comment);
    }

    private static string? NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? GuessTitleFromStreamInfo(StreamInfo streamInfo)
    {
        if (!string.IsNullOrWhiteSpace(streamInfo.FileName))
        {
            return Path.GetFileNameWithoutExtension(streamInfo.FileName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(streamInfo.LocalPath))
        {
            return Path.GetFileNameWithoutExtension(streamInfo.LocalPath.Trim());
        }

        if (!string.IsNullOrWhiteSpace(streamInfo.Url))
        {
            return streamInfo.Url.Trim();
        }

        return null;
    }

    private static string? ExtractTitleFromSegments(IReadOnlyList<DocumentSegment> segments)
    {
        if (segments is null)
        {
            return null;
        }

        foreach (var segment in segments)
        {
            if (segment is null || string.IsNullOrWhiteSpace(segment.Markdown))
            {
                continue;
            }

            if (segment.Type == SegmentType.Image)
            {
                continue;
            }

            using var reader = new StringReader(segment.Markdown);
            string? line;
            var insideComment = false;
            while ((line = reader.ReadLine()) is not null)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (insideComment)
                {
                    if (trimmed.Contains("-->", StringComparison.Ordinal))
                    {
                        insideComment = false;
                    }

                    continue;
                }

                if (trimmed.StartsWith("<!--", StringComparison.Ordinal))
                {
                    insideComment = !trimmed.Contains("-->", StringComparison.Ordinal);
                    continue;
                }

                if (IsImagePlaceholder(trimmed))
                {
                    continue;
                }

                if (trimmed.StartsWith('#'))
                {
                    return trimmed.TrimStart('#').Trim();
                }

                if (!trimmed.StartsWith('>'))
                {
                    return trimmed;
                }
            }
        }

        return null;
    }

    private static string? BuildAnnotation(DocumentSegment segment)
    {
        var tags = new List<string>();

        if (segment.Number.HasValue)
        {
            var key = segment.Type switch
            {
                SegmentType.Page => SegmentAnnotationTokens.Page,
                SegmentType.Slide => SegmentAnnotationTokens.Slide,
                SegmentType.Sheet => SegmentAnnotationTokens.Sheet,
                SegmentType.Table => SegmentAnnotationTokens.Table,
                SegmentType.Section => SegmentAnnotationTokens.Section,
                SegmentType.Chapter => SegmentAnnotationTokens.Chapter,
                SegmentType.Image => SegmentAnnotationTokens.Image,
                SegmentType.Metadata => SegmentAnnotationTokens.Metadata,
                SegmentType.Audio => SegmentAnnotationTokens.Segment,
                _ => SegmentAnnotationTokens.Segment
            };

            tags.Add($"{key}:{segment.Number.Value}");
        }

        if (segment.Type == SegmentType.Audio)
        {
            if (segment.StartTime.HasValue && segment.EndTime.HasValue)
            {
                tags.Add($"{SegmentAnnotationTokens.Timecode}:{FormatTime(segment.StartTime.Value)}-{FormatTime(segment.EndTime.Value)}");
            }
            else if (segment.StartTime.HasValue)
            {
                tags.Add($"{SegmentAnnotationTokens.Timecode}:{FormatTime(segment.StartTime.Value)}");
            }
        }
        else
        {
            if (segment.StartTime.HasValue)
            {
                tags.Add($"{SegmentAnnotationTokens.Start}:{FormatTime(segment.StartTime.Value)}");
            }
            if (segment.EndTime.HasValue)
            {
                tags.Add($"{SegmentAnnotationTokens.End}:{FormatTime(segment.EndTime.Value)}");
            }
        }

        if (!string.IsNullOrWhiteSpace(segment.Label))
        {
            tags.Add($"{SegmentAnnotationTokens.Label}:{Sanitize(segment.Label!)}");
        }

        if (!string.IsNullOrWhiteSpace(segment.Source))
        {
            tags.Add($"{SegmentAnnotationTokens.Source}:{Sanitize(segment.Source!)}");
        }

        if (segment.AdditionalMetadata.Count > 0)
        {
            foreach (var pair in segment.AdditionalMetadata)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                var key = Sanitize(pair.Key);
                var value = Sanitize(pair.Value);

                if (tags.Exists(tag => tag.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                tags.Add($"{key}:{value}");
            }
        }

        if (tags.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var tag in tags)
        {
            builder.Append('[').Append(tag).Append(']').Append(' ');
        }

        return builder.ToString().TrimEnd();
    }

    private static bool IsImagePlaceholder(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("![", StringComparison.Ordinal))
        {
            return true;
        }

        trimmed = trimmed.TrimStart('>');
        trimmed = trimmed.TrimStart();
        return trimmed.StartsWith("**Image", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatTime(TimeSpan value)
        => value.ToString(value.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);

    private static string Sanitize(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (char.IsWhiteSpace(ch))
            {
                builder.Append('_');
            }
            else if (ch is '[' or ']' or ':' or '\n' or '\r' or '\t')
            {
                builder.Append('-');
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
