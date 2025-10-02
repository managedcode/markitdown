namespace MarkItDown;

/// <summary>
/// Represents the semantic type of a <see cref="DocumentSegment"/>.
/// </summary>
public enum SegmentType
{
    /// <summary>
    /// Unknown or uncategorized segment type.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Segment containing the content of a single page.
    /// </summary>
    Page,

    /// <summary>
    /// Segment representing a single slide in a presentation.
    /// </summary>
    Slide,

    /// <summary>
    /// Segment representing a worksheet/sheet in a spreadsheet.
    /// </summary>
    Sheet,

    /// <summary>
    /// Segment representing a logical section heading or grouping.
    /// </summary>
    Section,

    /// <summary>
    /// Segment containing tabular data.
    /// </summary>
    Table,

    /// <summary>
    /// Segment representing a chapter or chapter-like portion of a document.
    /// </summary>
    Chapter,

    /// <summary>
    /// Segment representing audio transcript content.
    /// </summary>
    Audio,

    /// <summary>
    /// Segment representing an embedded image or rendered page snapshot.
    /// </summary>
    Image,

    /// <summary>
    /// Segment representing metadata about the source document.
    /// </summary>
    Metadata,
}
