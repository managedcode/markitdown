using System.Text;

namespace MarkItDown.Core.Converters;

/// <summary>
/// Converts audio files to Markdown by extracting metadata and optional transcription text.
/// </summary>
public sealed class AudioConverter : IDocumentConverter
{
    private static readonly HashSet<string> AcceptedExtensions =
    [
        ".wav",
        ".mp3",
        ".m4a",
        ".mp4",
    ];

    private static readonly HashSet<string> AcceptedMimePrefixes =
    [
        "audio/",
        "video/mp4",
    ];

    private static readonly string[] MetadataFields =
    {
        "Title",
        "Artist",
        "Author",
        "Band",
        "Album",
        "Genre",
        "Track",
        "DateTimeOriginal",
        "CreateDate",
        "Duration",
        "NumChannels",
        "SampleRate",
        "AvgBytesPerSec",
        "BitsPerSample",
    };

    private readonly string? _exifToolPath;
    private readonly Func<byte[], StreamInfo, CancellationToken, Task<string?>>? _transcribeAsync;

    public AudioConverter(string? exifToolPath = null, Func<byte[], StreamInfo, CancellationToken, Task<string?>>? transcribeAsync = null)
    {
        _exifToolPath = exifToolPath;
        _transcribeAsync = transcribeAsync;
    }

    public int Priority => 460;

    public bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
        => AcceptsInput(streamInfo);

    public bool AcceptsInput(StreamInfo streamInfo)
    {
        var extension = streamInfo.Extension?.ToLowerInvariant();
        if (extension is not null && AcceptedExtensions.Contains(extension))
        {
            return true;
        }

        var mime = (streamInfo.MimeType ?? string.Empty).ToLowerInvariant();
        return AcceptedMimePrefixes.Any(mime.StartsWith);
    }

    public async Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        var bytes = memory.ToArray();

        var metadata = await ExifToolMetadataExtractor.ExtractAsync(bytes, streamInfo.Extension, _exifToolPath, cancellationToken).ConfigureAwait(false);
        var builder = new StringBuilder();

        foreach (var field in MetadataFields)
        {
            if (metadata.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                builder.Append(field).Append(':').Append(' ').AppendLine(value.Trim());
            }
        }

        var transcript = await TryTranscribeAsync(bytes, streamInfo, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(transcript))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine("### Audio Transcript");
            builder.AppendLine();
            builder.AppendLine(transcript.Trim());
        }

        var markdown = builder.Length > 0 ? builder.ToString().TrimEnd() : "*No audio metadata available.*";
        var title = metadata.TryGetValue("Title", out var t) && !string.IsNullOrWhiteSpace(t)
            ? t.Trim()
            : streamInfo.FileName is not null ? Path.GetFileNameWithoutExtension(streamInfo.FileName) : null;

        return new DocumentConverterResult(markdown, title);
    }

    private async Task<string?> TryTranscribeAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        if (_transcribeAsync is null)
        {
            return null;
        }

        try
        {
            return await _transcribeAsync(audioBytes, streamInfo, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}
