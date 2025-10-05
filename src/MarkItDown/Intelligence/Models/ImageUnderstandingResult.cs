namespace MarkItDown.Intelligence.Models;

/// <summary>
/// Represents the outcome of analyzing an image with a provider (captions, OCR, objects, etc.).
/// </summary>
public sealed class ImageUnderstandingResult
{
    public ImageUnderstandingResult(
        string? caption,
        string? text,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<string>? detectedObjects = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Caption = caption;
        Text = text;
        Tags = tags ?? Array.Empty<string>();
        DetectedObjects = detectedObjects ?? Array.Empty<string>();
        Metadata = metadata ?? new Dictionary<string, string>();
    }

    public string? Caption { get; }

    public string? Text { get; }

    public IReadOnlyList<string> Tags { get; }

    public IReadOnlyList<string> DetectedObjects { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}
