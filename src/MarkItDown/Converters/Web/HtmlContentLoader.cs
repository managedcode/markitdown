using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MarkItDown.Converters;

internal static class HtmlContentLoader
{
    public static async Task<string> ReadHtmlAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(streamInfo);

        if (!stream.CanSeek)
        {
            await using var handle = await DiskBufferHandle.FromStreamAsync(stream, streamInfo.Extension, bufferSize: 64 * 1024, onChunkWritten: null, cancellationToken).ConfigureAwait(false);
            await using var local = handle.OpenRead();
            return await ReadHtmlFromSeekableStreamAsync(local, streamInfo, cancellationToken).ConfigureAwait(false);
        }

        return await ReadHtmlFromSeekableStreamAsync(stream, streamInfo, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadHtmlFromSeekableStreamAsync(Stream stream, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        if (!stream.CanSeek)
        {
            throw new InvalidOperationException("Stream must support seeking after materialisation.");
        }

        var encoding = streamInfo.Charset ??
                        await DetectEncodingAsync(stream, cancellationToken).ConfigureAwait(false) ??
                        Encoding.UTF8;

        stream.Position = 0;
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
#if NET9_0_OR_GREATER
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
#else
        return await reader.ReadToEndAsync().ConfigureAwait(false);
#endif
    }

    private static async Task<Encoding?> DetectEncodingAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var originalPosition = stream.Position;

        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
        stream.Position = originalPosition;

        if (read == 0)
        {
            return null;
        }

        return DetectEncoding(buffer.AsSpan(0, read));
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
