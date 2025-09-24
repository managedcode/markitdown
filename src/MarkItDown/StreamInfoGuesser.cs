using System.Globalization;
using System.Text;

namespace MarkItDown;

internal static class StreamInfoGuesser
{
    private const int SampleSize = 16 * 1024;

    public static IReadOnlyList<StreamInfo> Guess(Stream stream, StreamInfo baseInfo)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(baseInfo);

        var normalized = baseInfo.CopyWith(
            mimeType: baseInfo.MimeType ?? GuessMimeFromExtension(baseInfo.Extension),
            extension: baseInfo.Extension ?? GuessExtensionFromMime(baseInfo.MimeType));

        var guesses = new List<StreamInfo>();
        var sample = ReadSample(stream);

        var contentGuess = GuessFromContent(sample, normalized);
        if (contentGuess is null)
        {
            guesses.Add(normalized);
            return guesses;
        }

        if (IsCompatible(normalized, contentGuess))
        {
            guesses.Add(normalized.CopyWith(contentGuess));
        }
        else
        {
            guesses.Add(normalized);
            guesses.Add(normalized.CopyWith(contentGuess));
        }

        return guesses;
    }

    private static StreamInfo? GuessFromContent(ReadOnlyMemory<byte> sample, StreamInfo baseInfo)
    {
        if (sample.IsEmpty)
        {
            return null;
        }

        var (encoding, textSample) = TryDecodeText(sample.Span);

        if (IsPdf(sample.Span))
        {
            return new StreamInfo("application/pdf", ".pdf", charset: encoding);
        }

        if (IsZip(sample.Span))
        {
            var extension = GuessZipExtension(baseInfo.FileName);
            var mime = extension is null ? "application/zip" : MimeMapping.GetMimeType(extension) ?? "application/zip";
            return new StreamInfo(mime, extension ?? ".zip", charset: null);
        }

        if (IsPng(sample.Span))
        {
            return new StreamInfo("image/png", ".png");
        }

        if (IsJpeg(sample.Span))
        {
            return new StreamInfo("image/jpeg", ".jpg");
        }

        if (IsGif(sample.Span))
        {
            return new StreamInfo("image/gif", ".gif");
        }

        if (IsWebP(sample.Span))
        {
            return new StreamInfo("image/webp", ".webp");
        }

        if (IsPlainText(textSample))
        {
            var looksLikeHtml = LooksLikeHtml(textSample);
            if (looksLikeHtml)
            {
                return new StreamInfo("text/html", ".html", encoding);
            }

            if (LooksLikeJson(textSample))
            {
                return new StreamInfo("application/json", ".json", encoding);
            }

            if (LooksLikeXml(textSample))
            {
                return new StreamInfo("application/xml", ".xml", encoding);
            }

            if (LooksLikeCsv(textSample))
            {
                return new StreamInfo("text/csv", ".csv", encoding);
            }

            return new StreamInfo("text/plain", ".txt", encoding);
        }

        return null;
    }

    private static (Encoding? Encoding, string Text) TryDecodeText(ReadOnlySpan<byte> sample)
    {
        if (sample.IsEmpty)
        {
            return (null, string.Empty);
        }

        Encoding? encoding = null;

        if (sample.Length >= 4)
        {
            if (sample[0] == 0x00 && sample[1] == 0x00 && sample[2] == 0xFE && sample[3] == 0xFF)
            {
                encoding = Encoding.GetEncoding("utf-32BE");
            }
            else if (sample[0] == 0xFF && sample[1] == 0xFE && sample[2] == 0x00 && sample[3] == 0x00)
            {
                encoding = Encoding.UTF32;
            }
        }

        if (encoding is null && sample.Length >= 2)
        {
            if (sample[0] == 0xFE && sample[1] == 0xFF)
            {
                encoding = Encoding.BigEndianUnicode;
            }
            else if (sample[0] == 0xFF && sample[1] == 0xFE)
            {
                encoding = Encoding.Unicode;
            }
        }

        if (encoding is null && sample.Length >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF)
        {
            encoding = Encoding.UTF8;
        }

        encoding ??= Encoding.UTF8;

        try
        {
            return (encoding, encoding.GetString(sample));
        }
        catch
        {
            return (encoding, string.Empty);
        }
    }

    private static bool LooksLikeHtml(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("<!DOCTYPE HTML", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (trimmed.StartsWith("<head", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("<body", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeJson(string text)
    {
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch))
            {
                return ch is '{' or '[';
            }
        }

        return false;
    }

    private static bool LooksLikeXml(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return trimmed.StartsWith('<') && trimmed.Contains('>');
    }

    private static bool LooksLikeCsv(string text)
    {
        var lines = text.Split('\n');
        if (lines.Length < 2)
        {
            return false;
        }

        static int CountSeparators(string line)
        {
            var separatorCount = 0;
            foreach (var ch in line)
            {
                if (ch is ',' or ';' or '\t')
                {
                    separatorCount++;
                }
            }

            return separatorCount;
        }

        var first = CountSeparators(lines[0]);
        var second = CountSeparators(lines[1]);
        return first > 0 && second > 0;
    }

    private static bool IsPlainText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var gracefulLength = Math.Min(text.Length, 1024);
        for (var i = 0; i < gracefulLength; i++)
        {
            var ch = text[i];
            if (char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPdf(ReadOnlySpan<byte> sample)
    {
        return sample.Length > 4 && sample[0] == '%' && sample[1] == 'P' && sample[2] == 'D' && sample[3] == 'F';
    }

    private static bool IsZip(ReadOnlySpan<byte> sample)
    {
        return sample.Length > 4 && sample[0] == 'P' && sample[1] == 'K';
    }

    private static bool IsPng(ReadOnlySpan<byte> sample)
    {
        return sample.Length >= 8 && sample[..8].SequenceEqual(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });
    }

    private static bool IsJpeg(ReadOnlySpan<byte> sample)
    {
        return sample.Length >= 3 && sample[0] == 0xFF && sample[1] == 0xD8 && sample[2] == 0xFF;
    }

    private static bool IsGif(ReadOnlySpan<byte> sample)
    {
        if (sample.Length < 6)
        {
            return false;
        }

        var header = sample[..6];
        return header.SequenceEqual("GIF87a"u8) || header.SequenceEqual("GIF89a"u8);
    }

    private static bool IsWebP(ReadOnlySpan<byte> sample)
    {
        return sample.Length >= 12 && sample[..4].SequenceEqual("RIFF"u8) && sample[8..12].SequenceEqual("WEBP"u8);
    }

    private static string? GuessZipExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".docx" => ".docx",
            ".pptx" => ".pptx",
            ".xlsx" => ".xlsx",
            ".epub" => ".epub",
            _ => ".zip"
        };
    }

    private static bool IsCompatible(StreamInfo baseInfo, StreamInfo candidate)
    {
        if (!string.IsNullOrEmpty(baseInfo.MimeType) && !string.IsNullOrEmpty(candidate.MimeType) &&
            !string.Equals(baseInfo.MimeType, candidate.MimeType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(baseInfo.Extension) && !string.IsNullOrEmpty(candidate.Extension) &&
            !string.Equals(baseInfo.Extension, candidate.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (baseInfo.Charset is not null && candidate.Charset is not null &&
            !string.Equals(baseInfo.Charset.WebName, candidate.Charset.WebName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string? GuessExtensionFromMime(string? mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
        {
            return null;
        }

        var extension = MimeMapping.GetExtension(mime);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        return extension.StartsWith('.') ? extension : "." + extension;
    }

    private static string? GuessMimeFromExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var ext = extension.StartsWith('.') ? extension : "." + extension;
        return MimeMapping.GetMimeType(ext);
    }

    private static ReadOnlyMemory<byte> ReadSample(Stream stream)
    {
        var buffer = new byte[SampleSize];
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        try
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            if (bytesRead == buffer.Length)
            {
                return new ReadOnlyMemory<byte>(buffer);
            }

            var slice = new byte[bytesRead];
            Array.Copy(buffer, slice, bytesRead);
            return new ReadOnlyMemory<byte>(slice);
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = originalPosition;
            }
        }
    }
}
