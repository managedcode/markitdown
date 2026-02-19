using MarkItDown.Intelligence;

namespace MarkItDown.Converters;

/// <summary>
/// Converts uploaded video media by delegating transcript extraction to the configured media provider (e.g. Azure Video Indexer).
/// </summary>
public sealed class VideoConverter : DocumentConverterBase
{
    private static readonly HashSet<string> AcceptedExtensions =
    [
        ".mp4",
        ".mov",
        ".m4v",
        ".avi",
        ".mkv",
        ".webm",
    ];

    private static readonly string[] AcceptedMimePrefixes =
    [
        "video/",
    ];

    private readonly AudioConverter delegateConverter;

    public VideoConverter(
        string? exifToolPath = null,
        Func<byte[], StreamInfo, CancellationToken, Task<string?>>? transcribeAsync = null,
        SegmentOptions? segmentOptions = null,
        IMediaTranscriptionProvider? mediaProvider = null)
        : base(priority: 465)
    {
        delegateConverter = new AudioConverter(exifToolPath, transcribeAsync, segmentOptions, mediaProvider);
    }

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var extension = streamInfo.Extension?.ToLowerInvariant();
        if (extension is not null && AcceptedExtensions.Contains(extension))
        {
            return true;
        }

        var mime = streamInfo.MimeType?.ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(mime) && AcceptedMimePrefixes.Any(mime.StartsWith);
    }

    public override Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        => delegateConverter.ConvertAsync(stream, streamInfo, cancellationToken);
}
