namespace MarkItDown;

/// <summary>
/// The result of converting a document to Markdown.
/// </summary>
public sealed class DocumentConverterResult
{
    /// <summary>
    /// Initialize the DocumentConverterResult.
    /// </summary>
    /// <param name="markdown">The converted Markdown text.</param>
    /// <param name="title">Optional title of the document.</param>
    public DocumentConverterResult(string markdown, string? title = null)
    {
        Markdown = markdown ?? throw new ArgumentNullException(nameof(markdown));
        Title = title;
    }

    /// <summary>
    /// The converted Markdown text.
    /// </summary>
    public string Markdown { get; }

    /// <summary>
    /// Optional title of the document.
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// Soft-deprecated alias for Markdown. New code should migrate to using Markdown property.
    /// </summary>
    public string TextContent => Markdown;

    /// <summary>
    /// Return the converted Markdown text.
    /// </summary>
    public override string ToString() => Markdown;
}
