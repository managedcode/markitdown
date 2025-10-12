using MarkItDown.Intelligence.Models;

namespace MarkItDown.Intelligence;

/// <summary>
/// Provides advanced image analysis such as OCR, captioning, and object recognition.
/// </summary>
public interface IImageUnderstandingProvider
{
    Task<ImageUnderstandingResult?> AnalyzeAsync(Stream stream, StreamInfo streamInfo, ImageUnderstandingRequest? request = null, CancellationToken cancellationToken = default);
}
