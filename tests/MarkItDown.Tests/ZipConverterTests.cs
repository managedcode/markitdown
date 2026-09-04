using System.IO.Compression;
using MarkItDown.Converters;
using Shouldly;

namespace MarkItDown.Tests;

public sealed class ZipConverterTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConvertAsync_CanceledArchive_PropagatesCancellation(bool cancelBeforeConversion)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cancellation-{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("last-entry.txt", CompressionLevel.NoCompression).Open());
                writer.Write("Cancellation while copying the final archive entry must not produce a successful conversion.");
            }

            using var cancellation = new CancellationTokenSource();
            if (cancelBeforeConversion)
            {
                cancellation.Cancel();
            }

            await using var source = new CancelOnAsyncReadFileStream(path, cancellation);
            var converter = new ZipConverter(new[] { new PlainTextConverter() });

            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converter.ConvertAsync(
                source, new StreamInfo(fileName: "cancellation.zip", extension: ".zip"), cancellation.Token));

            cancellation.IsCancellationRequested.ShouldBeTrue();
            exception.CancellationToken.ShouldBe(cancellation.Token);
            source.CanceledDuringEntryRead.ShouldBe(!cancelBeforeConversion);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class CancelOnAsyncReadFileStream(string path, CancellationTokenSource cancellation)
        : FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
    {
        public bool CanceledDuringEntryRead { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var bytesRead = await base.ReadAsync(buffer, cancellationToken);
            CanceledDuringEntryRead = true;
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return bytesRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var bytesRead = await base.ReadAsync(buffer, offset, count, cancellationToken);
            CanceledDuringEntryRead = true;
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return bytesRead;
        }
    }
}
