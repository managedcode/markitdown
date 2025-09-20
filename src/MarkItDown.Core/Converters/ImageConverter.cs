using System.Text;

namespace MarkItDown.Core.Converters;

/// <summary>
/// Converts common image formats to Markdown by extracting EXIF metadata and optional captions.
/// </summary>
public sealed class ImageConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".bmp",
        ".tiff",
        ".tif",
        ".webp",
    ];

    private static readonly string[] MetadataFields =
    {
        "ImageSize",
        "Title",
        "Caption",
        "Description",
        "Keywords",
        "Artist",
        "Author",
        "DateTimeOriginal",
        "CreateDate",
        "GPSPosition",
    };

    private readonly string? _exifToolPath;
    private readonly Func<byte[], StreamInfo, CancellationToken, Task<string?>>? _describeImageAsync;

    public ImageConverter(string? exifToolPath = null, Func<byte[], StreamInfo, CancellationToken, Task<string?>>? describeImageAsync = null)
    {
        _exifToolPath = exifToolPath;
        _describeImageAsync = describeImageAsync;
    }

    public int Priority => 450;

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        => AcceptsInput(streamInfo);

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var extension = streamInfo.Extension?.ToLowerInvariant();
        if (extension is not null && AcceptedExtensions.Contains(extension))
        {
            return true;
        }

        var mimeType = streamInfo.MimeType?.ToLowerInvariant() ?? string.Empty;
        if (mimeType.StartsWith("image/", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var imageBytes = memory.ToArray();

        var builder = new StringBuilder();
        var metadata = await ExifToolMetadataExtractor.ExtractAsync(imageBytes, streamInfo.Extension, _exifToolPath, cancellationToken).ConfigureAwait(false);
        foreach (var field in MetadataFields)
        {
            if (metadata.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                builder.Append(field).Append(':').Append(' ').AppendLine(value.Trim());
            }
        }

        if (_describeImageAsync is not null)
        {
            var description = await SafeDescribeAsync(imageBytes, streamInfo, cancellationToken);
            if (!string.IsNullOrWhiteSpace(description))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendLine("### Image Description");
                builder.AppendLine();
                builder.AppendLine(description.Trim());
            }
        }

        var markdown = builder.Length > 0
            ? builder.ToString().TrimEnd()
            : "*No image metadata available.*";

        var title = TryGuessTitle(metadata, streamInfo);
        return new DocumentConverterResult(markdown, title);
    }

    private async Task<string?> SafeDescribeAsync(byte[] imageBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        if (_describeImageAsync is null)
        {
            return null;
        }

        try
        {
            return await _describeImageAsync(imageBytes, streamInfo, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGuessTitle(IReadOnlyDictionary<string, string> metadata, StreamInfo streamInfo)
    {
        foreach (var key in new[] { "Title", "Caption", "Description" })
        {
            if (metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(streamInfo.FileName))
        {
            return Path.GetFileNameWithoutExtension(streamInfo.FileName);
        }

        return null;
    }

}
