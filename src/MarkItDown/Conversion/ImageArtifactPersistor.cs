using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace MarkItDown;

/// <summary>
/// Persists <see cref="ImageArtifact"/> instances to an <see cref="ArtifactWorkspace"/>.
/// </summary>
internal sealed class ImageArtifactPersistor
{
    private readonly ArtifactWorkspace workspace;
    private readonly string filePrefix;
    private int counter;

    public ImageArtifactPersistor(ArtifactWorkspace workspace, StreamInfo streamInfo)
    {
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        streamInfo = streamInfo ?? throw new ArgumentNullException(nameof(streamInfo));

        var candidate = streamInfo.FileName ?? streamInfo.LocalPath ?? streamInfo.Url;
        var baseName = string.IsNullOrWhiteSpace(candidate)
            ? "image"
            : Path.GetFileNameWithoutExtension(candidate) ?? "image";
        filePrefix = SanitizeStem(baseName);
    }

    public ArtifactWorkspace Workspace => workspace;

    public void Persist(ImageArtifact artifact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();

        var extension = GuessImageExtension(artifact.ContentType) ?? ".bin";
        if (!extension.StartsWith(".", StringComparison.Ordinal))
        {
            extension = $".{extension}";
        }

        var index = Interlocked.Increment(ref counter);
        var fileName = $"{filePrefix}_{index:D4}{extension}";
        var destination = workspace.PersistBinary(fileName, artifact.Data, artifact.ContentType, cancellationToken);

        artifact.FilePath = destination;
        artifact.Metadata[MetadataKeys.ArtifactPath] = destination;
        artifact.Metadata[MetadataKeys.ArtifactFileName] = fileName;
        artifact.Metadata[MetadataKeys.ArtifactRelativePath] = fileName;
    }

    private static string SanitizeStem(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "image";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        var sanitized = builder.ToString().Trim('_');
        return string.IsNullOrEmpty(sanitized) ? "image" : sanitized;
    }

    private static string? GuessImageExtension(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        if (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
        {
            return ".png";
        }

        if (contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
            contentType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase))
        {
            return ".jpg";
        }

        if (contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase))
        {
            return ".gif";
        }

        if (contentType.Equals("image/bmp", StringComparison.OrdinalIgnoreCase))
        {
            return ".bmp";
        }

        if (contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase))
        {
            return ".webp";
        }

        if (contentType.Equals("image/tiff", StringComparison.OrdinalIgnoreCase) ||
            contentType.Equals("image/tif", StringComparison.OrdinalIgnoreCase))
        {
            return ".tiff";
        }

        return null;
    }
}
