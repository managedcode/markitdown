namespace MarkItDown;

/// <summary>
/// Interface for document converters that can convert various file formats to Markdown.
/// </summary>
public interface IDocumentConverter
{
    /// <summary>
    /// Gets the priority of this converter. Lower values are tried first.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Return a quick determination on if the converter should attempt converting the document.
    /// This is primarily based on streamInfo (typically, streamInfo.MimeType, streamInfo.Extension).
    /// </summary>
    /// <param name="streamInfo">The StreamInfo object containing metadata about the file.</param>
    /// <returns>True if the converter can handle the document, False otherwise.</returns>
    bool AcceptsInput(StreamInfo streamInfo);

    /// <summary>
    /// Return a quick determination on if the converter should attempt converting the document.
    /// This is primarily based on streamInfo (typically, streamInfo.MimeType, streamInfo.Extension).
    /// </summary>
    /// <param name="stream">The file stream to convert. Must support seeking.</param>
    /// <param name="streamInfo">The StreamInfo object containing metadata about the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the converter can handle the document, False otherwise.</returns>
    bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        // Default implementation delegates to AcceptsInput for backward compatibility
        return AcceptsInput(streamInfo);
    }

    /// <summary>
    /// Convert a document to Markdown text.
    /// </summary>
    /// <param name="stream">The file stream to convert. Must support seeking.</param>
    /// <param name="streamInfo">The StreamInfo object containing metadata about the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the conversion, which includes the title and markdown content.</returns>
    /// <exception cref="FileConversionException">If the mimetype is recognized, but the conversion fails for some other reason.</exception>
    /// <exception cref="UnsupportedFormatException">If the converter cannot handle this format.</exception>
    Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default);
}