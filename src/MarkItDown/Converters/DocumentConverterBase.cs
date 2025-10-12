using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ManagedCode.MimeTypes;

namespace MarkItDown.Converters;

/// <summary>
/// Provides a reusable foundation for document converter implementations, including shared
/// validation and helper methods for common MIME and extension matching patterns.
/// </summary>
public abstract class DocumentConverterBase
{
    protected DocumentConverterBase(int priority)
    {
        Priority = priority;
    }

    public int Priority { get; }

    public abstract bool AcceptsInput(StreamInfo streamInfo);

    public virtual bool Accepts(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(streamInfo);

        return AcceptsInput(streamInfo);
    }

    public abstract Task<DocumentConverterResult> ConvertAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Helper that verifies whether the supplied <paramref name="streamInfo"/> matches any of the provided extensions.
    /// </summary>
    /// <param name="streamInfo">The stream metadata to inspect.</param>
    /// <param name="acceptedExtensions">Set of accepted extensions (include leading dot).</param>
    /// <returns><see langword="true"/> when the extension matches one of the accepted values.</returns>
    protected static bool MatchesExtension(StreamInfo streamInfo, ISet<string> acceptedExtensions)
    {
        if (acceptedExtensions is null || acceptedExtensions.Count == 0)
        {
            return false;
        }

        var extension = streamInfo.Extension?.Trim();
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        foreach (var candidate in acceptedExtensions)
        {
            if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Helper that verifies whether the supplied <paramref name="streamInfo"/> matches any of the provided MIME types.
    /// </summary>
    /// <param name="streamInfo">The stream metadata to inspect.</param>
    /// <param name="acceptedMimeTypes">Set of accepted MIME types.</param>
    /// <returns><see langword="true"/> when the MIME matches one of the accepted values.</returns>
    protected static bool MatchesMimeType(StreamInfo streamInfo, ISet<string> acceptedMimeTypes)
    {
        if (acceptedMimeTypes is null || acceptedMimeTypes.Count == 0)
        {
            return false;
        }

        var normalized = streamInfo.ResolveMimeType();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            foreach (var candidate in acceptedMimeTypes)
            {
                if (string.Equals(normalized, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        var mime = streamInfo.MimeType;
        if (!string.IsNullOrWhiteSpace(mime))
        {
            foreach (var candidate in acceptedMimeTypes)
            {
                if (string.Equals(mime, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
