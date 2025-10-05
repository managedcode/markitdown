using MarkItDown.Intelligence.Models;

namespace MarkItDown.Intelligence;

/// <summary>
/// Provides transcription services for audio or video content.
/// </summary>
public interface IMediaTranscriptionProvider
{
    Task<MediaTranscriptionResult?> TranscribeAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default);
}
