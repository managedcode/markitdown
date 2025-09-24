using System.Text;

namespace MarkItDown;

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
    /// <param name="fileName">The filename.</param>
    /// <param name="localPath">The local file path.</param>
    /// <param name="url">The URL where the file was retrieved from.</param>
    public StreamInfo(
        string? mimeType = null,
        string? extension = null,
        Encoding? charset = null,
        string? fileName = null,
        string? localPath = null,
        string? url = null)
    {
        MimeType = mimeType;
        Extension = NormalizeExtension(extension);
        Charset = charset;
        FileName = fileName;
        LocalPath = localPath;
        Url = url;
    }

    /// <summary>
    /// Create StreamInfo with string charset.
    /// </summary>
    public static StreamInfo WithCharset(
        string? mimeType,
        string? extension,
        string? charset,
        string? fileName = null,
        string? localPath = null,
        string? url = null)
    {
        return new StreamInfo(
            mimeType: mimeType,
            extension: extension,
            charset: TryParseCharset(charset),
            fileName: fileName,
            localPath: localPath,
            url: url);
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
    public string? FileName { get; }

    /// <summary>
    /// The local file path.
    /// </summary>
    public string? LocalPath { get; }

    /// <summary>
    /// The URL where the file was retrieved from.
    /// </summary>
    public string? Url { get; }

    /// <summary>
    /// Create a shallow copy of this <see cref="StreamInfo"/> while optionally overriding selected fields.
    /// </summary>
    /// <param name="other">Another <see cref="StreamInfo"/> whose non-null fields take precedence over the current instance.</param>
    /// <param name="mimeType">Optional MIME type override.</param>
    /// <param name="extension">Optional file extension override.</param>
    /// <param name="charset">Optional charset override.</param>
    /// <param name="fileName">Optional file name override.</param>
    /// <param name="localPath">Optional local path override.</param>
    /// <param name="url">Optional URL override.</param>
    /// <returns>A new <see cref="StreamInfo"/> instance with the updated values.</returns>
    public StreamInfo CopyWith(
        StreamInfo? other = null,
        string? mimeType = null,
        string? extension = null,
        Encoding? charset = null,
        string? fileName = null,
        string? localPath = null,
        string? url = null)
    {
        var mergedMime = mimeType ?? other?.MimeType ?? MimeType;
        var mergedExtension = NormalizeExtension(extension ?? other?.Extension ?? Extension);
        var mergedCharset = charset ?? other?.Charset ?? Charset;
        var mergedFileName = fileName ?? other?.FileName ?? FileName;
        var mergedLocalPath = localPath ?? other?.LocalPath ?? LocalPath;
        var mergedUrl = url ?? other?.Url ?? Url;

        return new StreamInfo(mergedMime, mergedExtension, mergedCharset, mergedFileName, mergedLocalPath, mergedUrl);
    }

    /// <summary>
    /// Copy and update this instance using a charset expressed as a string.
    /// </summary>
    /// <param name="charset">The charset to apply.</param>
    /// <returns>A new <see cref="StreamInfo"/> instance with the charset applied.</returns>
    public StreamInfo CopyWithCharset(string? charset)
    {
        return CopyWith(charset: TryParseCharset(charset));
    }

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

    /// <summary>
    /// Try to parse a charset string into an Encoding object.
    /// </summary>
    /// <param name="charset">The charset name.</param>
    /// <returns>The Encoding object, or null if parsing failed.</returns>
    private static Encoding? TryParseCharset(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
            return null;

        try
        {
            return Encoding.GetEncoding(charset);
        }
        catch
        {
            return null;
        }
    }
}
