using System;
using System.Collections.Generic;
using ManagedCode.MimeTypes;

namespace MarkItDown;

internal static class MimeTypeUtilities
{
    private const string OctetStream = "application/octet-stream";

    public static string WithSubtype(string baseMime, string newSubtype)
    {
        var type = GetTypeSegment(baseMime);
        return Compose(type, newSubtype);
    }

    public static string WithType(string baseMime, string newType)
    {
        var subtype = GetSubtypeSegment(baseMime);
        return Compose(newType, subtype);
    }

    public static string Compose(string type, string subtype)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return subtype;
        }

        return string.IsNullOrWhiteSpace(subtype) ? type : $"{type}/{subtype}";
    }

    public static string? NormalizeMime(StreamInfo streamInfo)
    {
        ArgumentNullException.ThrowIfNull(streamInfo);

        return TryResolveViaExtension(streamInfo.Extension)
            ?? TryResolveViaFileName(streamInfo.FileName)
            ?? TryResolveViaFileName(streamInfo.LocalPath)
            ?? streamInfo.MimeType;
    }

    public static bool MatchesAny(string? candidate, IEnumerable<string> acceptedPrefixes)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        foreach (var prefix in acceptedPrefixes)
        {
            if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryResolveViaExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var candidate = MimeHelper.GetMimeType(extension);
        return IsKnown(candidate) ? candidate : null;
    }

    private static string? TryResolveViaFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var candidate = MimeHelper.GetMimeType(fileName);
        return IsKnown(candidate) ? candidate : null;
    }

    private static string GetTypeSegment(string mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
        {
            return string.Empty;
        }

        var separatorIndex = mime.IndexOf('/');
        return separatorIndex >= 0 ? mime[..separatorIndex] : mime;
    }

    private static string GetSubtypeSegment(string mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
        {
            return string.Empty;
        }

        var separatorIndex = mime.IndexOf('/');
        return separatorIndex >= 0 && separatorIndex < mime.Length - 1
            ? mime[(separatorIndex + 1)..]
            : string.Empty;
    }

    private static bool IsKnown(string? mime)
    {
        return !string.IsNullOrWhiteSpace(mime) && !string.Equals(mime, OctetStream, StringComparison.OrdinalIgnoreCase);
    }
}
