namespace MarkItDown.Core;

/// <summary>
/// Priority constants for converter ordering. Lower priority values are tried first.
/// </summary>
public static class ConverterPriority
{
    /// <summary>
    /// Priority for specific file format converters (e.g., .docx, .pdf, .xlsx) or specific pages (e.g., Wikipedia).
    /// </summary>
    public const double SpecificFileFormat = 0.0;

    /// <summary>
    /// Priority for generic file format converters (near catch-all converters for mimetypes like text/*).
    /// </summary>
    public const double GenericFileFormat = 10.0;
}

/// <summary>
/// A registration of a converter with its priority and other metadata.
/// </summary>
public sealed record ConverterRegistration(IDocumentConverter Converter, double Priority)
{
    /// <summary>
    /// Gets the converter instance.
    /// </summary>
    public IDocumentConverter Converter { get; } = Converter ?? throw new ArgumentNullException(nameof(Converter));

    /// <summary>
    /// Gets the priority of this converter. Lower values are tried first.
    /// </summary>
    public double Priority { get; } = Priority;
}