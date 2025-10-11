using System.Collections.ObjectModel;

namespace MarkItDown;

/// <summary>
/// Represents the raw artifacts extracted during conversion prior to Markdown composition.
/// </summary>
public sealed class ConversionArtifacts
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConversionArtifacts"/> class.
    /// </summary>
    public ConversionArtifacts()
    {
        TextBlocks = new List<TextArtifact>();
        Tables = new List<TableArtifact>();
        Images = new List<ImageArtifact>();
        Metadata = new Dictionary<string, string>();
    }

    private ConversionArtifacts(bool _)
    {
        TextBlocks = EmptyTextBlocks;
        Tables = EmptyTables;
        Images = EmptyImages;
        Metadata = EmptyMetadata;
    }

    /// <summary>
    /// Gets a reusable empty instance.
    /// </summary>
    public static ConversionArtifacts Empty { get; } = new(true);

    private static readonly IList<TextArtifact> EmptyTextBlocks = new ReadOnlyCollection<TextArtifact>(Array.Empty<TextArtifact>());
    private static readonly IList<TableArtifact> EmptyTables = new ReadOnlyCollection<TableArtifact>(Array.Empty<TableArtifact>());
    private static readonly IList<ImageArtifact> EmptyImages = new ReadOnlyCollection<ImageArtifact>(Array.Empty<ImageArtifact>());
    private static readonly IDictionary<string, string> EmptyMetadata = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    /// <summary>
    /// Gets the raw text artifacts captured from the source.
    /// </summary>
    public IList<TextArtifact> TextBlocks { get; }

    /// <summary>
    /// Gets the tabular artifacts captured from the source.
    /// </summary>
    public IList<TableArtifact> Tables { get; }

    /// <summary>
    /// Gets the image artifacts captured from the source.
    /// </summary>
    public IList<ImageArtifact> Images { get; }

    /// <summary>
    /// Gets conversion-level metadata surfaced by the converter.
    /// </summary>
    public IDictionary<string, string> Metadata { get; }
}

/// <summary>
/// Represents a block of text extracted from the source document.
/// </summary>
public sealed class TextArtifact
{
    public TextArtifact(string text, int? pageNumber = null, string? source = null, string? label = null)
    {
        Text = text ?? string.Empty;
        PageNumber = pageNumber;
        Source = source;
        Label = label;
    }

    public string Text { get; set; }

    public int? PageNumber { get; set; }

    public string? Source { get; set; }

    public string? Label { get; set; }
}

/// <summary>
/// Represents tabular content extracted from the source document.
/// </summary>
public sealed class TableArtifact
{
    public TableArtifact(IList<IList<string>> rows, int? pageNumber = null, string? source = null, string? label = null)
    {
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        PageNumber = pageNumber;
        Source = source;
        Label = label;
    }

    public IList<IList<string>> Rows { get; }

    public int? PageNumber { get; set; }

    public string? Source { get; set; }

    public string? Label { get; set; }
}

/// <summary>
/// Represents an image extracted from the source document.
/// </summary>
public sealed class ImageArtifact
{
    public ImageArtifact(byte[] data, string? contentType = null, int? pageNumber = null, string? source = null, string? label = null)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        ContentType = contentType;
        PageNumber = pageNumber;
        Source = source;
        Label = label;
        Metadata = new Dictionary<string, string>();
    }

    /// <summary>
    /// Gets the raw binary data for the image.
    /// </summary>
    public byte[] Data { get; }

    /// <summary>
    /// Gets the content type associated with the image.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Gets or sets the page number that owns the image, when applicable.
    /// </summary>
    public int? PageNumber { get; set; }

    /// <summary>
    /// Gets or sets the logical source identifier for the image.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets the friendly label for the image.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Gets or sets the enriched description generated for the image.
    /// </summary>
    public string? DetailedDescription { get; set; }

    /// <summary>
    /// Gets or sets a Mermaid diagram representation when the image depicts structured data.
    /// </summary>
    public string? MermaidDiagram { get; set; }

    /// <summary>
    /// Gets or sets additional textual extraction (such as OCR output).
    /// </summary>
    public string? RawText { get; set; }

    /// <summary>
    /// Gets metadata describing the image artifact.
    /// </summary>
    public IDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Gets or sets the segment index that references this artifact within the composed output.
    /// </summary>
    public int? SegmentIndex { get; set; }

    /// <summary>
    /// Gets or sets the Markdown placeholder that was emitted during extraction for this image.
    /// </summary>
    public string? PlaceholderMarkdown { get; set; }
}
