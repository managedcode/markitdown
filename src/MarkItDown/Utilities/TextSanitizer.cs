using System;
using System.Text;

namespace MarkItDown;

internal static class TextSanitizer
{
    public static string Normalize(string? value, bool trim = true, bool collapseWhitespaceAroundLineBreaks = true)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var span = value.AsSpan();
        var builder = new StringBuilder(span.Length);

        foreach (var ch in span)
        {
            switch (ch)
            {
                case '\r':
                    builder.Append('\n');
                    break;
                case '\t':
                case '\u00A0': // non-breaking space
                case '\u202F': // narrow non-breaking space
                case '\u2007': // figure space
                    builder.Append(' ');
                    break;
                case '\u200B': // zero-width space
                case '\u200C': // zero-width non-joiner
                case '\u200D': // zero-width joiner
                case '\u2060': // word joiner
                case '\uFEFF': // BOM
                case '\u00AD': // soft hyphen
                    break; // strip
                default:
                    builder.Append(ch);
                    break;
            }
        }

        var normalized = builder.ToString();

        if (collapseWhitespaceAroundLineBreaks)
        {
            normalized = CollapseWhitespaceAroundNewlines(normalized);
        }

        if (trim)
        {
            normalized = normalized.Trim();
        }
        else if (normalized.Length > 0 && string.IsNullOrWhiteSpace(normalized))
        {
            normalized = string.Empty;
        }

        if (string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return normalized;
    }

    private static string CollapseWhitespaceAroundNewlines(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var span = value.AsSpan();
        var builder = new StringBuilder(span.Length);
        var pendingSpace = false;
        var previousWasNewline = false;
        var newlineRun = 0;

        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];

            if (ch == ' ')
            {
                pendingSpace = true;
                continue;
            }

            if (ch == '\n')
            {
                if (pendingSpace)
                {
                    pendingSpace = false;
                }

                newlineRun++;
                if (newlineRun > 2)
                {
                    continue;
                }

                builder.Append('\n');
                previousWasNewline = true;
                continue;
            }

            if (pendingSpace && builder.Length > 0 && !previousWasNewline)
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            previousWasNewline = false;
            newlineRun = 0;
            builder.Append(ch);
        }

        if (pendingSpace && builder.Length > 0)
        {
            builder.Append(' ');
        }

        return builder.ToString();
    }
}
