using System.Text;

namespace MarkItDown.Core.Converters;

internal static class HtmlContentLoader
{
    public static async Task<string> ReadHtmlAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        if (streamInfo.Charset is not null)
        {
            using var reader = new StreamReader(stream, streamInfo.Charset, leaveOpen: true);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        using var bufferStream = new MemoryStream();
        await stream.CopyToAsync(bufferStream, cancellationToken).ConfigureAwait(false);
        var buffer = bufferStream.ToArray();

        var encoding = DetectEncoding(buffer) ?? Encoding.UTF8;
        return encoding.GetString(buffer);
    }

    private static Encoding? DetectEncoding(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[..3].SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            return Encoding.UTF8;
        }

        if (data.Length >= 4 && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0xFE && data[3] == 0xFF)
        {
            return Encoding.GetEncoding("utf-32BE");
        }

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xFE && data[2] == 0x00 && data[3] == 0x00)
        {
            return Encoding.UTF32;
        }

        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        return null;
    }
}
