using System;
using System.IO;
using System.Text;

namespace MarkItDown;

/// <summary>
/// Provides helpers to generate filesystem-safe file names.
/// </summary>
internal static class FileNameSanitizer
{
    public static string BuildFileName(string? candidate, string fallbackBaseName, string? extension = null)
    {
        var baseComponent = string.IsNullOrWhiteSpace(candidate)
            ? fallbackBaseName
            : Path.GetFileNameWithoutExtension(candidate);

        var sanitized = SanitizeSegment(baseComponent);
        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = fallbackBaseName;
        }

        var normalizedExtension = NormalizeExtension(extension ?? Path.GetExtension(candidate));
        return string.IsNullOrEmpty(normalizedExtension)
            ? sanitized
            : $"{sanitized}{normalizedExtension}";
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        extension = extension.Trim();
        if (!extension.StartsWith('.'))
        {
            extension = "." + extension;
        }

        extension = extension.Replace(Path.DirectorySeparatorChar.ToString(), string.Empty, StringComparison.Ordinal)
                             .Replace(Path.AltDirectorySeparatorChar.ToString(), string.Empty, StringComparison.Ordinal);

        return extension;
    }

    private static string SanitizeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        var lastAppended = '\0';

        foreach (var ch in value)
        {
            var next = Array.IndexOf(invalid, ch) >= 0 || char.IsControl(ch) ? '_' : ch;
            if (next == '_' && lastAppended == '_')
            {
                continue;
            }

            builder.Append(next);
            lastAppended = next;
        }

        var sanitized = builder.ToString().Trim('_', '.');
        return sanitized.Length == 0 ? string.Empty : sanitized;
    }
}
