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
                SegmentType.Page => "page",
                SegmentType.Slide => "slide",
                SegmentType.Sheet => "sheet",
                SegmentType.Table => "table",
                SegmentType.Section => "section",
                SegmentType.Chapter => "chapter",
                SegmentType.Image => "image",
                SegmentType.Metadata => "meta",
                SegmentType.Audio => "segment",
                _ => "segment"
            };

            tags.Add($"{key}:{segment.Number.Value}");
        }

        if (segment.Type == SegmentType.Audio)
        {
            if (segment.StartTime.HasValue && segment.EndTime.HasValue)
            {
                tags.Add($"timecode:{FormatTime(segment.StartTime.Value)}-{FormatTime(segment.EndTime.Value)}");
            }
            else if (segment.StartTime.HasValue)
            {
                tags.Add($"timecode:{FormatTime(segment.StartTime.Value)}");
            }
        }
        else
        {
            if (segment.StartTime.HasValue)
            {
                tags.Add($"start:{FormatTime(segment.StartTime.Value)}");
            }
            if (segment.EndTime.HasValue)
            {
                tags.Add($"end:{FormatTime(segment.EndTime.Value)}");
            }
        }

        if (!string.IsNullOrWhiteSpace(segment.Label))
        {
            tags.Add($"label:{Sanitize(segment.Label!)}");
        }

        if (!string.IsNullOrWhiteSpace(segment.Source))
        {
            tags.Add($"source:{Sanitize(segment.Source!)}");
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
