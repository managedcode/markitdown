using System.Buffers.Text;
using System.Text;

namespace MarkItDown.Core;

internal static class UriUtilities
{
    public static string ResolveFilePath(string fileUri)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileUri);
        if (!fileUri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("URI is not a file URI.", nameof(fileUri));
        }

        var uri = new Uri(fileUri);
        if (!uri.IsFile)
        {
            throw new ArgumentException("The provided URI is not a file URI.", nameof(fileUri));
        }

        return uri.LocalPath;
    }

    public static DataUriPayload ParseDataUri(string dataUri)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataUri);
        if (!dataUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("URI is not a data URI.", nameof(dataUri));
        }

        var commaIndex = dataUri.IndexOf(',');
        if (commaIndex < 0 || commaIndex == dataUri.Length - 1)
        {
            throw new FormatException("Invalid data URI.");
        }

        var metadataSection = dataUri.Substring(5, commaIndex - 5); // strip "data:"
        var dataSection = dataUri.Substring(commaIndex + 1);

        var parts = metadataSection.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? mimeType = null;
        string? charset = null;
        var isBase64 = false;

        foreach (var part in parts)
        {
            if (part.Equals("base64", StringComparison.OrdinalIgnoreCase))
            {
                isBase64 = true;
                continue;
            }

            if (part.Contains('=') && part.StartsWith("charset", StringComparison.OrdinalIgnoreCase))
            {
                var kvp = part.Split('=', 2);
                charset = kvp.Length == 2 ? kvp[1] : null;
                continue;
            }

            mimeType ??= part;
        }

        mimeType ??= "text/plain";

        byte[] data;
        if (isBase64)
        {
            data = Convert.FromBase64String(dataSection);
        }
        else
        {
            data = Encoding.UTF8.GetBytes(Uri.UnescapeDataString(dataSection));
        }

        return new DataUriPayload(mimeType, charset, data);
    }
}

internal readonly record struct DataUriPayload(string MimeType, string? Charset, byte[] Data);
