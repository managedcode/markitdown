using System;

namespace MarkItDown;

/// <summary>
/// Options that control how segmented markdown output is composed.
/// </summary>
public sealed record SegmentOptions
{
    /// <summary>
    /// Controls whether segment metadata annotations should be emitted inline with markdown output.
    /// </summary>
    public bool IncludeSegmentMetadataInMarkdown { get; init; }

    /// <summary>
    /// Converter-specific options for audio content.
    /// </summary>
    public AudioSegmentOptions Audio { get; init; } = AudioSegmentOptions.Default;

    /// <summary>
    /// Provides a default instance.
    /// </summary>
    public static SegmentOptions Default => new();
}

/// <summary>
/// Options related to segmenting audio (and other timed media) content.
/// </summary>
public sealed record AudioSegmentOptions
{
    /// <summary>
    /// Duration used when slicing audio transcripts into segments. Defaults to one minute.
    /// </summary>
    public TimeSpan SegmentDuration { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Provides a default instance.
    /// </summary>
    public static AudioSegmentOptions Default => new();
}
