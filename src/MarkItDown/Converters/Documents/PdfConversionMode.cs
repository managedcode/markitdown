namespace MarkItDown.Converters;

/// <summary>
/// Controls how <see cref="PdfConverter"/> extracts content from PDF documents.
/// </summary>
public enum PdfConversionMode
{
    /// <summary>
    /// Runs structured parsing (text + tables + inline images) using the configured providers.
    /// </summary>
    Structured,

    /// <summary>
    /// Renders every page to an image, runs it through OCR/image understanding, and composes the results.
    /// </summary>
    RenderedPageOcr
}
