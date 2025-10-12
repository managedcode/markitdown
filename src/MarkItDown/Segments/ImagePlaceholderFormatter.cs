using System;
using System.IO;
using System.Text;

namespace MarkItDown;

/// <summary>
/// Builds Markdown placeholders for image artifacts.
/// </summary>
internal static class ImagePlaceholderFormatter
{
    public static string BuildPlaceholder(ImageArtifact image, string? summary, string? contextLabel = null)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        var normalizedSummary = TextSanitizer.Normalize(summary, trim: true);
        var normalizedContext = TextSanitizer.Normalize(contextLabel, trim: true);

        var altText = ResolveAltText(image, normalizedSummary, normalizedContext);

        var target = ResolveArtifactTarget(image);
        if (!string.IsNullOrWhiteSpace(target))
        {
            var escapedAlt = EscapeAltText(altText);
            var escapedTarget = EscapeLinkTarget(target!);
            return $"![{escapedAlt}]({escapedTarget})";
        }

        return $"**Image:** {altText}";
    }

    private static string ResolveAltText(ImageArtifact image, string? summary, string? contextLabel)
    {
        var label = summary;
        if (string.IsNullOrWhiteSpace(label))
        {
            label = BuildDefaultLabel(image);
        }
        else if (!string.IsNullOrWhiteSpace(contextLabel))
        {
            label = $"{contextLabel}: {label}";
        }

        return label!;
    }

    private static string BuildDefaultLabel(ImageArtifact image)
    {
        if (image.PageNumber.HasValue)
        {
            return $"Image on page {image.PageNumber.Value}";
        }

        if (image.Metadata.TryGetValue(MetadataKeys.Page, out var page) && !string.IsNullOrWhiteSpace(page))
        {
            return $"Image on page {page.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(image.Label))
        {
            return image.Label!;
        }

        return "Image";
    }

    private static string? ResolveArtifactTarget(ImageArtifact image)
    {
        if (image.Metadata.TryGetValue(MetadataKeys.ArtifactRelativePath, out var relativePath) &&
            !string.IsNullOrWhiteSpace(relativePath))
        {
            return NormalizePath(relativePath);
        }

        if (image.Metadata.TryGetValue(MetadataKeys.ArtifactFileName, out var fileName) &&
            !string.IsNullOrWhiteSpace(fileName))
        {
            return NormalizePath(fileName);
        }

        if (!string.IsNullOrWhiteSpace(image.FilePath))
        {
            var file = Path.GetFileName(image.FilePath);
            if (!string.IsNullOrWhiteSpace(file))
            {
                return NormalizePath(file);
            }

            return NormalizePath(image.FilePath);
        }

        if (image.Metadata.TryGetValue(MetadataKeys.ArtifactPath, out var absolutePath) &&
            !string.IsNullOrWhiteSpace(absolutePath))
        {
            var file = Path.GetFileName(absolutePath);
            return NormalizePath(string.IsNullOrWhiteSpace(file) ? absolutePath : file);
        }

        return null;
    }

    private static string NormalizePath(string value)
        => value.Replace('\\', '/').Trim();

    private static string EscapeAltText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Image";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '[':
                case ']':
                case '\\':
                    builder.Append('\\').Append(ch);
                    break;
                case '\r':
                case '\n':
                    builder.Append(' ');
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    private static string EscapeLinkTarget(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return Uri.EscapeDataString(value).Replace("%2F", "/");
    }
}
