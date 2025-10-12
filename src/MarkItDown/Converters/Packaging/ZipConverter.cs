using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ManagedCode.MimeTypes;
using MarkItDown;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for ZIP files that extracts and converts all contained files.
/// </summary>
public sealed class ZipConverter : DocumentConverterBase
{
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip"
    };

    private static readonly IReadOnlyCollection<string> AcceptedMimeTypePrefixes = new List<string>
    {
        MimeHelper.ZIP,
        "application/x-zip-compressed",
    };

    // Converters that we'll use for processing files within the ZIP
    private readonly List<DocumentConverterBase> _innerConverters;

    public ZipConverter(IEnumerable<DocumentConverterBase>? innerConverters = null)
        : base(priority: 400) // Process before generic converters
    {
        _innerConverters = innerConverters?.ToList() ?? new List<DocumentConverterBase>();
    }

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var normalizedMime = streamInfo.ResolveMimeType();
        var extension = streamInfo.Extension?.ToLowerInvariant();

        if (extension is not null && AcceptedExtensions.Contains(extension))
            return true;

        return StreamInfo.MatchesMime(normalizedMime, AcceptedMimeTypePrefixes)
            || (normalizedMime is null && StreamInfo.MatchesMime(streamInfo.MimeType, AcceptedMimeTypePrefixes));
    }

    public override bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
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

    public override async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            // Reset stream position
            if (stream.CanSeek)
                stream.Position = 0;

            var markdown = new StringBuilder();
            var segments = new List<DocumentSegment>();
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
                    await ProcessZipEntry(entry, fileName, markdown, segments, cancellationToken);
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

            var headerText = processedFiles == 0
                ? $"# {title}"
                : $"# {title} ({processedFiles} of {totalFiles} files processed)";

            segments.Insert(0, new DocumentSegment(
                markdown: headerText,
                type: SegmentType.Section,
                label: title,
                source: fileName));

            return new DocumentConverterResult(
                markdown: markdown.ToString().TrimEnd(),
                title: title,
                segments: segments
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

    private async Task ProcessZipEntry(
        ZipArchiveEntry entry,
        string archiveName,
        StringBuilder markdown,
        List<DocumentSegment> segments,
        CancellationToken cancellationToken)
    {
        var headerMarkdown = BuildEntryHeader(entry);
        var entryMetadata = CreateEntryMetadata(entry, archiveName);

        markdown.Append(headerMarkdown);
        segments.Add(new DocumentSegment(
            markdown: headerMarkdown.TrimEnd(),
            type: SegmentType.Section,
            label: entry.FullName,
            source: entry.FullName,
            additionalMetadata: entryMetadata));

        if (entry.Length == 0)
        {
            AppendMessage("*Empty file*");
            markdown.AppendLine();
            return;
        }

        const long maxFileSize = 50 * 1024 * 1024; // 50MB
        if (entry.Length > maxFileSize)
        {
            AppendMessage($"*File too large to process ({FileUtilities.FormatFileSize(entry.Length)})*");
            markdown.AppendLine();
            return;
        }

        try
        {
            await using var entryStream = entry.Open();
            await using var bufferHandle = await DiskBufferHandle.FromStreamAsync(entryStream, Path.GetExtension(entry.Name), bufferSize: 128 * 1024, onChunkWritten: null, cancellationToken).ConfigureAwait(false);

            var fileExtension = Path.GetExtension(entry.Name);
            var fileName = entry.Name;
            var mimeType = MimeHelper.GetMimeType(fileName);
            if (string.IsNullOrWhiteSpace(mimeType) || string.Equals(mimeType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                mimeType = MimeHelper.GetMimeType(fileExtension);
                if (string.Equals(mimeType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
                {
                    mimeType = null;
                }
            }
            else if (string.Equals(mimeType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                mimeType = null;
            }

            var fileStreamInfo = new StreamInfo(
                mimeType: mimeType,
                extension: fileExtension,
                charset: null,
                fileName: fileName,
                url: null
            );

            DocumentConverterBase? converter;
            await using (var detectionStream = bufferHandle.OpenRead())
            {
                converter = FindConverter(detectionStream, fileStreamInfo, cancellationToken);
            }

            if (converter != null)
            {
                await using var conversionStream = bufferHandle.OpenRead();
                var result = await converter.ConvertAsync(conversionStream, fileStreamInfo, cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(result.Markdown))
                {
                    markdown.AppendLine(result.Markdown);
                }
                else
                {
                    AppendMessage("*File processed but no content extracted*");
                }

                if (result.Segments.Count > 0)
                {
                    foreach (var segment in RemapSegments(result, entry, archiveName))
                    {
                        segments.Add(segment);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(result.Markdown))
                {
                    segments.Add(CreateEntryContentSegment(result.Markdown.TrimEnd(), entry, archiveName));
                }
            }
            else
            {
                AppendMessage($"*No converter available for file type: {fileExtension}*");
            }
        }
        catch (Exception ex)
        {
            AppendMessage($"*Error processing file: {ex.Message}*");
        }

        markdown.AppendLine();

        void AppendMessage(string message)
        {
            markdown.AppendLine(message);
            segments.Add(CreateEntryContentSegment(message, entry, archiveName, SegmentType.Section));
        }
    }

    private static string BuildEntryHeader(ZipArchiveEntry entry)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"## File: {entry.FullName}");
        builder.AppendLine();

        if (entry.Length > 0)
        {
            builder.AppendLine($"**Size:** {FileUtilities.FormatFileSize(entry.Length)}");
        }

        if (entry.LastWriteTime != DateTimeOffset.MinValue)
        {
            builder.AppendLine($"**Last Modified:** {entry.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        }

        builder.AppendLine();
        return builder.ToString();
    }

    private static Dictionary<string, string> CreateEntryMetadata(ZipArchiveEntry entry, string archiveName)
    {
        var metadata = new Dictionary<string, string>
        {
            ["entry"] = entry.FullName,
            ["sizeBytes"] = entry.Length.ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrEmpty(archiveName))
        {
            metadata["archive"] = archiveName;
        }

        if (entry.LastWriteTime != DateTimeOffset.MinValue)
        {
            metadata["lastModifiedUtc"] = entry.LastWriteTime.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        }

        return metadata;
    }

    private static DocumentSegment CreateEntryContentSegment(string content, ZipArchiveEntry entry, string archiveName, SegmentType type = SegmentType.Unknown)
    {
        var metadata = CreateEntryMetadata(entry, archiveName);
        metadata["contentRole"] = type.ToString();

        return new DocumentSegment(
            markdown: content.TrimEnd(),
            type: type,
            label: entry.FullName,
            source: entry.FullName,
            additionalMetadata: metadata);
    }

    private static IEnumerable<DocumentSegment> RemapSegments(DocumentConverterResult result, ZipArchiveEntry entry, string archiveName)
    {
        foreach (var segment in result.Segments)
        {
            var metadata = segment.AdditionalMetadata.Count > 0
                ? new Dictionary<string, string>(segment.AdditionalMetadata, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            metadata["entry"] = entry.FullName;
            if (!string.IsNullOrEmpty(archiveName))
            {
                metadata["archive"] = archiveName;
            }

            if (!string.IsNullOrWhiteSpace(segment.Source))
            {
                metadata["originalSource"] = segment.Source!;
            }

            yield return new DocumentSegment(
                markdown: segment.Markdown,
                type: segment.Type,
                number: segment.Number,
                label: segment.Label,
                startTime: segment.StartTime,
                endTime: segment.EndTime,
                source: entry.FullName,
                additionalMetadata: metadata);
        }
    }

    private DocumentConverterBase? FindConverter(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken)
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
}
