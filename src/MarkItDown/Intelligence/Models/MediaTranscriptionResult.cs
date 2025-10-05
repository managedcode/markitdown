namespace MarkItDown.Intelligence.Models;

/// <summary>
/// Represents timed transcript output for audio or video content.
/// </summary>
public sealed class MediaTranscriptionResult
{
    public MediaTranscriptionResult(
        IReadOnlyList<MediaTranscriptSegment> segments,
        string? language = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Segments = segments ?? throw new ArgumentNullException(nameof(segments));
        Language = language;
        Metadata = metadata ?? new Dictionary<string, string>();
    }

    public IReadOnlyList<MediaTranscriptSegment> Segments { get; }

    public string? Language { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public string GetFullTranscript()
    {
        if (Segments.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(" ", Segments.Select(s => s.Text).Where(static t => !string.IsNullOrWhiteSpace(t)));
    }
}

/// <summary>
/// Single transcript span with optional timing information.
/// </summary>
public sealed class MediaTranscriptSegment
{
    public MediaTranscriptSegment(
        string text,
        TimeSpan? start = null,
        TimeSpan? end = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Text = text ?? string.Empty;
        Start = start;
        End = end;
        Metadata = metadata ?? new Dictionary<string, string>();
    }

    public string Text { get; }

    public TimeSpan? Start { get; }

    public TimeSpan? End { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}
