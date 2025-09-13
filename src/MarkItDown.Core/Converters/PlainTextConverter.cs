using System.Text;

namespace MarkItDown.Core.Converters;

/// <summary>
/// Converter for plain text files and text-based formats.
/// </summary>
public sealed class PlainTextConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".text", ".md", ".markdown", ".json", ".jsonl"
    };

    private static readonly HashSet<string> AcceptedMimeTypePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/", "application/json", "application/markdown"
    };

    public int Priority => 1000; // Generic converter, lowest priority

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var mimeType = streamInfo.MimeType?.ToLowerInvariant() ?? string.Empty;
        var extension = streamInfo.Extension?.ToLowerInvariant();

        // If we have a charset, we can safely assume it's text
        if (streamInfo.Charset is not null)
            return true;

        // Check the extension
        if (extension is not null && AcceptedExtensions.Contains(extension))
            return true;

        // Check the mimetype
        foreach (var prefix in AcceptedMimeTypePrefixes)
        {
            if (mimeType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        return AcceptsInput(streamInfo);
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        string textContent;

        if (streamInfo.Charset is not null)
        {
            using var reader = new StreamReader(stream, streamInfo.Charset, leaveOpen: true);
            textContent = await reader.ReadToEndAsync(cancellationToken);
        }
        else
        {
            // Try to detect encoding
            var buffer = new byte[stream.Length];
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            
            if (bytesRead > 0)
            {
                // Reset stream position
                stream.Position = 0;
                
                // Try UTF-8 first, then fall back to system default
                try
                {
                    textContent = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    // Check if it's valid UTF-8 by detecting BOM or checking for replacement characters
                    if (textContent.Contains('\ufffd') && buffer.Length > 3)
                    {
                        // If we have replacement characters and no BOM, try system encoding
                        textContent = Encoding.Default.GetString(buffer, 0, bytesRead);
                    }
                }
                catch
                {
                    textContent = Encoding.Default.GetString(buffer, 0, bytesRead);
                }
            }
            else
            {
                textContent = string.Empty;
            }
        }

        return new DocumentConverterResult(textContent);
    }
}