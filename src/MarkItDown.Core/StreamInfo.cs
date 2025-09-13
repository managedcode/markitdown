using System.Text;

namespace MarkItDown.Core;

/// <summary>
/// Metadata about a file stream containing information like mimetype, extension, charset, etc.
/// </summary>
public sealed class StreamInfo
{
    /// <summary>
    /// Initialize the StreamInfo.
    /// </summary>
    /// <param name="mimeType">The MIME type of the file.</param>
    /// <param name="extension">The file extension (with or without leading dot).</param>
    /// <param name="charset">The character encoding of the file.</param>
    /// <param name="filename">The filename.</param>
    /// <param name="url">The URL where the file was retrieved from.</param>
    public StreamInfo(
        string? mimeType = null,
        string? extension = null,
        Encoding? charset = null,
        string? filename = null,
        string? url = null)
    {
        MimeType = mimeType;
        Extension = NormalizeExtension(extension);
        Charset = charset;
        Filename = filename;
        Url = url;
    }

    /// <summary>
    /// The MIME type of the file.
    /// </summary>
    public string? MimeType { get; }

    /// <summary>
    /// The file extension (normalized with leading dot).
    /// </summary>
    public string? Extension { get; }

    /// <summary>
    /// The character encoding of the file.
    /// </summary>
    public Encoding? Charset { get; }

    /// <summary>
    /// The filename.
    /// </summary>
    public string? Filename { get; }

    /// <summary>
    /// The URL where the file was retrieved from.
    /// </summary>
    public string? Url { get; }

    /// <summary>
    /// Normalize the file extension to include a leading dot.
    /// </summary>
    /// <param name="extension">The extension to normalize.</param>
    /// <returns>The normalized extension with leading dot, or null if input was null/empty.</returns>
    private static string? NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        return extension.StartsWith('.') ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";
    }
}