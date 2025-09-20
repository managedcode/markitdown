namespace MarkItDown.Core;

using System.Linq;

internal static class MimeMapping
{
    private static readonly Dictionary<string, string> ExtensionToMime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".markdown"] = "text/markdown",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".xhtml"] = "application/xhtml+xml",
        [".json"] = "application/json",
        [".jsonl"] = "application/json",
        [".ndjson"] = "application/json",
        [".ipynb"] = "application/x-ipynb+json",
        [".xml"] = "application/xml",
        [".rss"] = "application/rss+xml",
        [".atom"] = "application/atom+xml",
        [".csv"] = "text/csv",
        [".zip"] = "application/zip",
        [".epub"] = "application/epub+zip",
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".tiff"] = "image/tiff",
        [".tif"] = "image/tiff",
        [".webp"] = "image/webp",
        [".wav"] = "audio/x-wav",
        [".mp3"] = "audio/mpeg",
        [".m4a"] = "audio/mp4",
        [".mp4"] = "video/mp4",
        [".msg"] = "application/vnd.ms-outlook",
    };

    private static readonly Dictionary<string, string> MimeToExtension = ExtensionToMime
        .GroupBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

    public static string? GetMimeType(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        return ExtensionToMime.TryGetValue(normalized, out var mime) ? mime : null;
    }

    public static string? GetExtension(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return null;
        }

        return MimeToExtension.TryGetValue(mimeType, out var extension) ? extension : null;
    }
}
