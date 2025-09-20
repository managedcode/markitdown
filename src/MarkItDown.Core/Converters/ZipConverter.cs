using System.IO.Compression;
using System.Text;
using MarkItDown.Core;

namespace MarkItDown.Core.Converters;

/// <summary>
/// Converter for ZIP files that extracts and converts all contained files.
/// </summary>
public sealed class ZipConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip"
    };

    private static readonly HashSet<string> AcceptedMimeTypePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/zip", "application/x-zip-compressed"
    };

    // Converters that we'll use for processing files within the ZIP
    private readonly List<IDocumentConverter> _innerConverters;

    public int Priority => 400; // Process before generic converters

    public ZipConverter(IEnumerable<IDocumentConverter>? innerConverters = null)
    {
        _innerConverters = innerConverters?.ToList() ?? new List<IDocumentConverter>();
    }

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var mimeType = streamInfo.MimeType?.ToLowerInvariant() ?? string.Empty;
        var extension = streamInfo.Extension?.ToLowerInvariant();

        // Check the extension first
        if (extension is not null && AcceptedExtensions.Contains(extension))
            return true;

        // Check the mime type
        foreach (var prefix in AcceptedMimeTypePrefixes)
        {
            if (mimeType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (!AcceptsInput(streamInfo))
            return false;

        // Try to validate this is actually a ZIP file by checking the header
        if (!stream.CanSeek)
            return true;

        try
        {
            var originalPosition = stream.Position;
            stream.Position = 0;

            // Check for ZIP file signature (PK)
            var buffer = new byte[4];
            var bytesRead = stream.Read(buffer, 0, 4);
            
            stream.Position = originalPosition;

            // ZIP files start with "PK" (0x50, 0x4B)
            return bytesRead >= 2 && buffer[0] == 0x50 && buffer[1] == 0x4B;
        }
        catch
        {
            if (stream.CanSeek)
                stream.Position = 0;
            return true;
        }
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            // Reset stream position
            if (stream.CanSeek)
                stream.Position = 0;

            var markdown = new StringBuilder();
            var fileName = streamInfo.FileName ?? "archive.zip";
            var title = $"Content from {fileName}";

            markdown.AppendLine($"# {title}");
            markdown.AppendLine();

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            
            var processedFiles = 0;
            var totalFiles = archive.Entries.Count;

            foreach (var entry in archive.Entries.OrderBy(e => e.FullName))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip directories
                if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                    continue;

                try
                {
                    await ProcessZipEntry(entry, markdown, cancellationToken);
                    processedFiles++;
                }
                catch (Exception ex)
                {
                    // Log the error but continue processing other files
                    markdown.AppendLine($"## File: {entry.FullName}");
                    markdown.AppendLine();
                    markdown.AppendLine($"*Error processing file: {ex.Message}*");
                    markdown.AppendLine();
                }
            }

            if (processedFiles == 0)
            {
                markdown.AppendLine("*No files could be processed from this archive.*");
            }
            else
            {
                markdown.Insert(title.Length + 4, $" ({processedFiles} of {totalFiles} files processed)");
            }

            return new DocumentConverterResult(
                markdown: markdown.ToString().TrimEnd(),
                title: title
            );
        }
        catch (InvalidDataException ex)
        {
            throw new FileConversionException($"Invalid ZIP file format: {ex.Message}", ex);
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
        {
            throw new FileConversionException($"Failed to convert ZIP file: {ex.Message}", ex);
        }
    }

    private async Task ProcessZipEntry(ZipArchiveEntry entry, StringBuilder markdown, CancellationToken cancellationToken)
    {
        markdown.AppendLine($"## File: {entry.FullName}");
        markdown.AppendLine();

        // Add basic file information
        if (entry.Length > 0)
        {
            markdown.AppendLine($"**Size:** {FormatFileSize(entry.Length)}");
        }

        if (entry.LastWriteTime != DateTimeOffset.MinValue)
        {
            markdown.AppendLine($"**Last Modified:** {entry.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        }

        markdown.AppendLine();

        // Skip empty files
        if (entry.Length == 0)
        {
            markdown.AppendLine("*Empty file*");
            markdown.AppendLine();
            return;
        }

        // Skip very large files to avoid memory issues
        const long maxFileSize = 50 * 1024 * 1024; // 50MB
        if (entry.Length > maxFileSize)
        {
            markdown.AppendLine($"*File too large to process ({FormatFileSize(entry.Length)})*");
            markdown.AppendLine();
            return;
        }

        try
        {
            using var entryStream = entry.Open();
            using var memoryStream = new MemoryStream();
            
            // Copy to memory stream so we can seek
            await entryStream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            // Create StreamInfo for the file
            var fileExtension = Path.GetExtension(entry.Name);
            var fileName = entry.Name;
            var mimeType = MimeMapping.GetMimeType(fileExtension);

            var fileStreamInfo = new StreamInfo(
                mimeType: mimeType,
                extension: fileExtension,
                charset: null,
                fileName: fileName,
                url: null
            );

            // Try to find a suitable converter
            var converter = FindConverter(memoryStream, fileStreamInfo, cancellationToken);
            
            if (converter != null)
            {
                memoryStream.Position = 0;
                var result = await converter.ConvertAsync(memoryStream, fileStreamInfo, cancellationToken);
                
                if (!string.IsNullOrWhiteSpace(result.Markdown))
                {
                    markdown.AppendLine(result.Markdown);
                }
                else
                {
                    markdown.AppendLine("*File processed but no content extracted*");
                }
            }
            else
            {
                markdown.AppendLine($"*No converter available for file type: {fileExtension}*");
            }
        }
        catch (Exception ex)
        {
            markdown.AppendLine($"*Error processing file: {ex.Message}*");
        }

        markdown.AppendLine();
    }

    private IDocumentConverter? FindConverter(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        foreach (var converter in _innerConverters.OrderBy(c => c.Priority))
        {
            try
            {
                if (stream.CanSeek)
                    stream.Position = 0;

                if (converter.Accepts(stream, streamInfo, cancellationToken))
                {
                    return converter;
                }
            }
            catch
            {
                // Continue to next converter if this one fails
                continue;
            }
        }

        return null;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}
