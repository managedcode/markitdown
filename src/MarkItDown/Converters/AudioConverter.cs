using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MarkItDown.Converters;

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

    private readonly IAudioMetadataExtractor metadataExtractor;
    private readonly IAudioTranscriber transcriber;

    public AudioConverter(string? exifToolPath = null, Func<byte[], StreamInfo, CancellationToken, Task<string?>>? transcribeAsync = null)
        : this(new ExifToolAudioMetadataExtractor(exifToolPath),
            transcribeAsync is null ? NoOpAudioTranscriber.Instance : new DelegateAudioTranscriber(transcribeAsync))
    {
    }

    internal AudioConverter(IAudioMetadataExtractor metadataExtractor, IAudioTranscriber transcriber)
    {
        this.metadataExtractor = metadataExtractor ?? throw new ArgumentNullException(nameof(metadataExtractor));
        this.transcriber = transcriber ?? throw new ArgumentNullException(nameof(transcriber));
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

        var metadata = await metadataExtractor.ExtractAsync(bytes, streamInfo, cancellationToken).ConfigureAwait(false);
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
        try
        {
            return await transcriber.TranscribeAsync(audioBytes, streamInfo, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    internal interface IAudioMetadataExtractor
    {
        Task<IReadOnlyDictionary<string, string>> ExtractAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken);
    }

    internal interface IAudioTranscriber
    {
        Task<string?> TranscribeAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken);
    }

    private sealed class ExifToolAudioMetadataExtractor : IAudioMetadataExtractor
    {
        private readonly string? exifToolPath;

        public ExifToolAudioMetadataExtractor(string? exifToolPath)
        {
            this.exifToolPath = exifToolPath;
        }

        public async Task<IReadOnlyDictionary<string, string>> ExtractAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
        {
            var result = await ExifToolMetadataExtractor
                .ExtractAsync(audioBytes, streamInfo.Extension, exifToolPath, cancellationToken)
                .ConfigureAwait(false);

            return result;
        }
    }

    private sealed class DelegateAudioTranscriber : IAudioTranscriber
    {
        private readonly Func<byte[], StreamInfo, CancellationToken, Task<string?>> factory;

        public DelegateAudioTranscriber(Func<byte[], StreamInfo, CancellationToken, Task<string?>> factory)
            => this.factory = factory;

        public Task<string?> TranscribeAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
            => factory(audioBytes, streamInfo, cancellationToken);
    }

    private sealed class NoOpAudioTranscriber : IAudioTranscriber
    {
        public static NoOpAudioTranscriber Instance { get; } = new();

        private NoOpAudioTranscriber()
        {
        }

        public Task<string?> TranscribeAsync(byte[] audioBytes, StreamInfo streamInfo, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);
    }
}
