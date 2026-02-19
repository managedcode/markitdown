using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MarkItDown;
using MarkItDown.YouTube;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for YouTube URLs that extracts video metadata and information.
/// Note: This converter extracts metadata only and does not download video content or transcriptions.
/// </summary>
public sealed class YouTubeUrlConverter(IYouTubeMetadataProvider? metadataProvider = null) : DocumentConverterBase(priority: 50)
{
    private static readonly Regex VideoIdRegex = new("^[a-zA-Z0-9_-]{11}$", RegexOptions.Compiled);
    private static readonly string[] mediaMimePrefixes = ["audio/", "video/"];

    private readonly IYouTubeMetadataProvider metadataProvider = metadataProvider ?? new YoutubeExplodeMetadataProvider();

    // High priority for specific URL patterns

    public override bool AcceptsInput(StreamInfo streamInfo)
    {
        var url = streamInfo.Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (streamInfo.MatchesMime(mediaMimePrefixes))
        {
            return false;
        }

        return TryExtractVideoId(url, out _);
    }

    public override async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = streamInfo.Url;
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new UnsupportedFormatException("Invalid YouTube URL format");
            }

            if (!TryExtractVideoId(url, out var videoId))
            {
                throw new FileConversionException("Could not extract video ID from YouTube URL");
            }

            var metadata = await metadataProvider.GetVideoAsync(videoId, cancellationToken).ConfigureAwait(false);
            return CreateResult(url, videoId, metadata);
        }
        catch (Exception ex) when (ex is not MarkItDownException)
        {
            throw new FileConversionException($"Failed to process YouTube URL: {ex.Message}", ex);
        }
    }

    private static bool TryExtractVideoId(string url, out string videoId)
    {
        videoId = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsSupportedHost(uri.Host))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        string? candidate = null;

        if (host.EndsWith("youtu.be", StringComparison.Ordinal))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
            {
                candidate = segments[0];
            }
        }
        else
        {
            if (TryGetQueryParameter(uri, "v", out var queryVideoId))
            {
                candidate = queryVideoId;
            }
            else if (TryGetPathBasedVideoId(uri, out var pathVideoId))
            {
                candidate = pathVideoId;
            }
        }

        candidate = NormalizeVideoIdCandidate(candidate);
        if (string.IsNullOrWhiteSpace(candidate) || !VideoIdRegex.IsMatch(candidate))
        {
            return false;
        }

        videoId = candidate;
        return true;
    }

    private static bool IsSupportedHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var normalizedHost = host.ToLowerInvariant();
        return normalizedHost is "youtube.com"
            or "www.youtube.com"
            or "m.youtube.com"
            or "music.youtube.com"
            or "youtu.be"
            or "www.youtu.be";
    }

    private static bool TryGetQueryParameter(Uri uri, string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(uri.Query))
        {
            return false;
        }

        var query = uri.Query;
        if (query.StartsWith("?", StringComparison.Ordinal))
        {
            query = query[1..];
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            string currentKey;
            string currentValue;
            if (separatorIndex < 0)
            {
                currentKey = Uri.UnescapeDataString(pair);
                currentValue = string.Empty;
            }
            else
            {
                currentKey = Uri.UnescapeDataString(pair[..separatorIndex]);
                currentValue = Uri.UnescapeDataString(pair[(separatorIndex + 1)..]);
            }

            if (string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
            {
                value = currentValue;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetPathBasedVideoId(Uri uri, out string videoId)
    {
        videoId = string.Empty;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        var first = segments[0].ToLowerInvariant();
        if (first is "shorts" or "embed" or "live")
        {
            videoId = segments[1];
            return true;
        }

        return false;
    }

    private static string NormalizeVideoIdCandidate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        var delimiterIndex = normalized.IndexOfAny(['?', '&', '#', '/']);
        if (delimiterIndex >= 0)
        {
            normalized = normalized[..delimiterIndex];
        }

        return normalized.Trim();
    }

    private static DocumentConverterResult CreateResult(string url, string videoId, YouTubeMetadata? metadata)
    {
        var markdown = new StringBuilder();
        var segments = new List<DocumentSegment>();

        if (metadata is not null)
        {
            markdown.AppendLine($"# {EscapeMarkdown(metadata.Title)}");
            markdown.AppendLine();

            markdown.AppendLine("## Overview");
            markdown.AppendLine($"- **Channel:** [{EscapeMarkdown(metadata.ChannelTitle)}]({metadata.ChannelUrl})");
            markdown.AppendLine($"- **Duration:** {FormatTime(metadata.Duration)}");
            if (metadata.UploadDate.HasValue)
            {
                markdown.AppendLine($"- **Published:** {metadata.UploadDate:yyyy-MM-dd}");
            }
            if (metadata.ViewCount.HasValue)
            {
                markdown.AppendLine($"- **Views:** {metadata.ViewCount.Value:N0}");
            }
            if (metadata.LikeCount.HasValue)
            {
                markdown.AppendLine($"- **Likes:** {metadata.LikeCount.Value:N0}");
            }
            if (metadata.Tags.Count > 0)
            {
                markdown.AppendLine($"- **Tags:** {string.Join(", ", metadata.Tags.Select(EscapeMarkdown))}");
            }

            markdown.AppendLine();
            markdown.AppendLine("## Links");
            markdown.AppendLine($"- Watch: {metadata.WatchUrl}");
            markdown.AppendLine($"- Embed: https://www.youtube.com/embed/{videoId}");
            if (metadata.Thumbnails.Count > 0)
            {
                markdown.AppendLine($"- Thumbnail: {metadata.Thumbnails[0]}");
            }
            markdown.AppendLine();

            if (!string.IsNullOrWhiteSpace(metadata.Description))
            {
                markdown.AppendLine("## Description");
                markdown.AppendLine($"```\n{metadata.Description}\n```\n");
            }

            if (metadata.Thumbnails.Count > 0)
            {
                markdown.AppendLine("## Preview");
                markdown.AppendLine($"![Thumbnail]({metadata.Thumbnails[0]})");
                markdown.AppendLine();
            }

            var metadataSegmentText = new StringBuilder()
                .AppendLine($"Channel: {metadata.ChannelTitle}")
                .AppendLine($"Published: {(metadata.UploadDate?.ToString("u", CultureInfo.InvariantCulture) ?? "unknown")}")
                .AppendLine($"Duration: {FormatTime(metadata.Duration)}")
                .ToString().TrimEnd();

            segments.Add(new DocumentSegment(
                metadataSegmentText,
                SegmentType.Metadata,
                number: 1,
                label: "Video Metadata",
                additionalMetadata: BuildMetadataDictionary(metadata)
            ));

            if (metadata.Captions.Count > 0)
            {
                markdown.AppendLine("## Captions");
                markdown.AppendLine("Auto-generated transcript snippets included below.");
                markdown.AppendLine();

                var index = 1;
                foreach (var caption in metadata.Captions)
                {
                    segments.Add(new DocumentSegment(
                        caption.Text,
                        SegmentType.Audio,
                        number: index,
                        label: $"Caption {index}",
                        startTime: caption.Start,
                        endTime: caption.End,
                        additionalMetadata: caption.Metadata
                    ));
                    index++;
                }
            }
        }
        else
        {
            // Fallback minimal markdown
            markdown.AppendLine("# YouTube Video");
            markdown.AppendLine();
            markdown.AppendLine($"**Video URL:** {url}");
            markdown.AppendLine($"**Video ID:** {videoId}");
            markdown.AppendLine();
        }

        // Append URL parameter information regardless of metadata availability
        var parameters = ExtractUrlParameters(url);
        if (parameters.Count > 0)
        {
            markdown.AppendLine("## URL Parameters");
            foreach (var kvp in parameters)
            {
                markdown.AppendLine($"- **{EscapeMarkdown(kvp.Key)}:** {EscapeMarkdown(kvp.Value)}");
            }
            markdown.AppendLine();
        }

        return new DocumentConverterResult(
            markdown: markdown.ToString().TrimEnd(),
            title: metadata?.Title ?? $"YouTube Video {videoId}",
            segments: segments
        );
    }

    private static Dictionary<string, string> BuildMetadataDictionary(YouTubeMetadata metadata)
    {
        var values = new Dictionary<string, string>(metadata.AdditionalMetadata, StringComparer.OrdinalIgnoreCase)
        {
            [MetadataKeys.VideoId] = metadata.VideoId,
            [MetadataKeys.Channel] = metadata.ChannelTitle,
            [MetadataKeys.ChannelUrl] = metadata.ChannelUrl.ToString(),
            [MetadataKeys.Provider] = MetadataValues.ProviderYouTube
        };

        if (metadata.Duration.HasValue)
        {
            values[MetadataKeys.Duration] = metadata.Duration.Value.ToString();
        }

        if (metadata.UploadDate.HasValue)
        {
            values[MetadataKeys.UploadDate] = metadata.UploadDate.Value.ToString("u", CultureInfo.InvariantCulture);
        }

        if (metadata.ViewCount.HasValue)
        {
            values[MetadataKeys.ViewCount] = metadata.ViewCount.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (metadata.LikeCount.HasValue)
        {
            values[MetadataKeys.LikeCount] = metadata.LikeCount.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (metadata.Tags.Count > 0)
        {
            values[MetadataKeys.Tags] = string.Join(",", metadata.Tags);
        }

        return values;
    }

    private static string EscapeMarkdown(string value) => value.Replace("*", "\\*").Replace("_", "\\_");

    private static string FormatTime(TimeSpan? value) => value.HasValue ? value.Value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : "unknown";

    private static Dictionary<string, string> ExtractUrlParameters(string url)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var uri = new Uri(url);
            var query = uri.Query;

            if (string.IsNullOrEmpty(query)) return parameters;

            // Remove the leading '?'
            if (query.StartsWith("?"))
                query = query.Substring(1);

            var pairs = query.Split('&');
            foreach (var pair in pairs)
            {
                var keyValue = pair.Split('=');
                if (keyValue.Length == 2)
                {
                    var key = Uri.UnescapeDataString(keyValue[0]);
                    var value = Uri.UnescapeDataString(keyValue[1]);
                    
                    // Add descriptive names for common YouTube parameters
                    var description = GetParameterDescription(key);
                    var displayKey = string.IsNullOrEmpty(description) ? key : $"{key} ({description})";
                    
                    parameters[displayKey] = value;
                }
            }
        }
        catch
        {
            // If URL parsing fails, return empty dictionary
        }

        return parameters;
    }

    private static string GetParameterDescription(string parameter) => parameter.ToLowerInvariant() switch
    {
        "v" => "Video ID",
        "t" => "Start Time",
        "list" => "Playlist ID",
        "index" => "Playlist Index",
        "ab_channel" => "Channel Name",
        "feature" => "Feature",
        "app" => "App",
        "si" => "Share ID",
        _ => string.Empty
    };
}
