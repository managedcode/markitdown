using System.Text;
using System.Text.RegularExpressions;

namespace MarkItDown.Converters;

/// <summary>
/// Converter for YouTube URLs that extracts video metadata and information.
/// Note: This converter extracts metadata only and does not download video content or transcriptions.
/// </summary>
public sealed class YouTubeUrlConverter : IDocumentConverter
{
    private static readonly Regex YouTubeUrlRegex = new(
        @"^https?://(www\.)?(youtube\.com/watch\?v=|youtu\.be/)([a-zA-Z0-9_-]{11})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public int Priority => 50; // High priority for specific URL patterns

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var url = streamInfo.Url;
        return !string.IsNullOrEmpty(url) && YouTubeUrlRegex.IsMatch(url);
    }

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        return AcceptsInput(streamInfo);
    }

    public Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = streamInfo.Url;
            if (string.IsNullOrEmpty(url) || !YouTubeUrlRegex.IsMatch(url))
            {
                throw new UnsupportedFormatException("Invalid YouTube URL format");
            }

            var videoId = ExtractVideoId(url);
            if (string.IsNullOrEmpty(videoId))
            {
                throw new FileConversionException("Could not extract video ID from YouTube URL");
            }

            var markdown = new StringBuilder();
            markdown.AppendLine($"# YouTube Video");
            markdown.AppendLine();

            // Add basic information
            markdown.AppendLine($"**Video URL:** {url}");
            markdown.AppendLine($"**Video ID:** {videoId}");
            markdown.AppendLine();

            // Extract URL parameters
            var urlParams = ExtractUrlParameters(url);
            if (urlParams.Count > 0)
            {
                markdown.AppendLine("## URL Parameters");
                foreach (var param in urlParams)
                {
                    markdown.AppendLine($"- **{param.Key}:** {param.Value}");
                }
                markdown.AppendLine();
            }

            // Add embedded video link
            markdown.AppendLine("## Video");
            markdown.AppendLine($"[![YouTube Video](https://img.youtube.com/vi/{videoId}/0.jpg)]({url})");
            markdown.AppendLine();

            // Add note about transcription
            markdown.AppendLine("## Note");
            markdown.AppendLine("*This converter extracts YouTube video metadata only. For video transcription, additional services would be required.*");
            markdown.AppendLine();
            markdown.AppendLine("**Alternative Access Methods:**");
            markdown.AppendLine($"- Watch URL: {url}");
            markdown.AppendLine($"- Embed URL: https://www.youtube.com/embed/{videoId}");
            markdown.AppendLine($"- Thumbnail URL: https://img.youtube.com/vi/{videoId}/maxresdefault.jpg");

            return Task.FromResult(new DocumentConverterResult(
                markdown: markdown.ToString().TrimEnd(),
                title: $"YouTube Video {videoId}"
            ));
        }
        catch (Exception ex) when (!(ex is MarkItDownException))
        {
            throw new FileConversionException($"Failed to process YouTube URL: {ex.Message}", ex);
        }
    }

    private static string ExtractVideoId(string url)
    {
        var match = YouTubeUrlRegex.Match(url);
        if (match.Success && match.Groups.Count > 3)
        {
            return match.Groups[3].Value;
        }

        // Fallback extraction methods
        if (url.Contains("youtube.com/watch"))
        {
            var vIndex = url.IndexOf("v=", StringComparison.OrdinalIgnoreCase);
            if (vIndex != -1)
            {
                var startIndex = vIndex + 2;
                var endIndex = url.IndexOf('&', startIndex);
                if (endIndex == -1) endIndex = url.Length;
                
                var videoId = url.Substring(startIndex, endIndex - startIndex);
                if (videoId.Length == 11) return videoId;
            }
        }
        else if (url.Contains("youtu.be/"))
        {
            var slashIndex = url.LastIndexOf('/');
            if (slashIndex != -1 && slashIndex < url.Length - 1)
            {
                var videoId = url.Substring(slashIndex + 1);
                var questionIndex = videoId.IndexOf('?');
                if (questionIndex != -1)
                {
                    videoId = videoId.Substring(0, questionIndex);
                }
                if (videoId.Length == 11) return videoId;
            }
        }

        return string.Empty;
    }

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

    private static string GetParameterDescription(string parameter)
    {
        return parameter.ToLowerInvariant() switch
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
}