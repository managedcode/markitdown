using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MarkItDown;

internal static class SegmentMarkdownComposer
{
    public static string Compose(IReadOnlyList<DocumentSegment> segments, SegmentOptions? options)
    {
        if (segments is null || segments.Count == 0)
        {
            return string.Empty;
        }

        var effectiveOptions = options ?? SegmentOptions.Default;
        var builder = new StringBuilder();
        var first = true;

        foreach (var segment in segments)
        {
            if (segment is null)
            {
                continue;
            }

            var content = segment.Markdown?.Trim();
            if (string.IsNullOrEmpty(content))
            {
                continue;
            }

            if (!first)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            if (effectiveOptions.IncludeSegmentMetadataInMarkdown)
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

        return builder.ToString().TrimEnd();
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

                // Skip if we already wrote a tag for this key (case-insensitive)
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
