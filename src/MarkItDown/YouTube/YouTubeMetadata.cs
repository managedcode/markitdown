using MarkItDown.Intelligence.Models;

namespace MarkItDown.YouTube;

/// <summary>
/// Rich metadata about a YouTube video, including optional captions.
/// </summary>
public sealed record YouTubeMetadata(
    string VideoId,
    string Title,
    string ChannelTitle,
    Uri WatchUrl,
    Uri ChannelUrl,
    TimeSpan? Duration,
    DateTimeOffset? UploadDate,
    long? ViewCount,
    long? LikeCount,
    IReadOnlyList<string> Tags,
    string? Description,
    IReadOnlyList<Uri> Thumbnails,
    IReadOnlyList<YouTubeCaptionSegment> Captions,
    IReadOnlyDictionary<string, string> AdditionalMetadata
);

/// <summary>
/// Represents a single caption segment returned for a YouTube video.
/// </summary>
public sealed record YouTubeCaptionSegment(string Text, TimeSpan? Start, TimeSpan? End, IReadOnlyDictionary<string, string> Metadata);
