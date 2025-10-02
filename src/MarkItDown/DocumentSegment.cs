using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MarkItDown;

/// <summary>
/// Represents a discrete piece of converted content (for example a page, slide, or audio slice).
/// </summary>
public sealed class DocumentSegment
{
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentSegment"/> class.
    /// </summary>
    /// <param name="markdown">Markdown content for this segment.</param>
    /// <param name="type">Semantic type for the segment.</param>
    /// <param name="number">Optional sequence number (page number, slide number, etc.).</param>
    /// <param name="label">Human-readable label for the segment.</param>
    /// <param name="startTime">Optional start time (for timed media).</param>
    /// <param name="endTime">Optional end time (for timed media).</param>
    /// <param name="source">Optional source identifier (file path, archive entry, etc.).</param>
    /// <param name="additionalMetadata">Additional metadata entries for the segment.</param>
    public DocumentSegment(
        string markdown,
        SegmentType type,
        int? number = null,
        string? label = null,
        TimeSpan? startTime = null,
        TimeSpan? endTime = null,
        string? source = null,
        IReadOnlyDictionary<string, string>? additionalMetadata = null)
    {
        Markdown = markdown ?? throw new ArgumentNullException(nameof(markdown));
        Type = type;
        Number = number;
        Label = label;
        StartTime = startTime;
        EndTime = endTime;
        Source = source;
        AdditionalMetadata = additionalMetadata is null
            ? EmptyMetadata
            : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(additionalMetadata));
    }

    /// <summary>
    /// Markdown content for this segment.
    /// </summary>
    public string Markdown { get; }

    /// <summary>
    /// Semantic type that describes the segment (page, slide, audio slice, etc.).
    /// </summary>
    public SegmentType Type { get; }

    /// <summary>
    /// Optional sequence number associated with the segment (page number, slide number, etc.).
    /// </summary>
    public int? Number { get; }

    /// <summary>
    /// Human-readable label or title for the segment.
    /// </summary>
    public string? Label { get; }

    /// <summary>
    /// Optional start time when the segment maps to timed media.
    /// </summary>
    public TimeSpan? StartTime { get; }

    /// <summary>
    /// Optional end time when the segment maps to timed media.
    /// </summary>
    public TimeSpan? EndTime { get; }

    /// <summary>
    /// Optional source identifier that produced the segment (file path, archive entry, etc.).
    /// </summary>
    public string? Source { get; }

    /// <summary>
    /// Additional metadata describing the segment.
    /// </summary>
    public IReadOnlyDictionary<string, string> AdditionalMetadata { get; }
}
