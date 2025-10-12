using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;
using Xunit;

namespace MarkItDown.Tests.Conversion;

public class DiskBufferHandleTests
{
    [Fact]
    public async Task FromStreamAsync_PersistsPayloadAndReportsLength()
    {
        var payload = Enumerable.Range(0, 1024).Select(i => (byte)(i % 251)).ToArray();
        await using var source = new NonSeekableStream(payload);

        long reported = 0;
        await using var handle = await DiskBufferHandle.FromStreamAsync(
            source,
            extensionHint: ".bin",
            bufferSize: 128,
            onChunkWritten: value => reported = value,
            cancellationToken: CancellationToken.None);

        Assert.Equal(payload.Length, handle.Length);
        Assert.Equal(payload.Length, reported);

        using var reader = handle.OpenRead();
        using var memory = new MemoryStream();
        await reader.CopyToAsync(memory);
        Assert.True(payload.SequenceEqual(memory.ToArray()));
    }

    [Fact]
    public async Task FromBytesAsync_WritesFileAndDisposesDirectory()
    {
        var payload = Enumerable.Range(0, 512).Select(i => (byte)(i % 199)).ToArray();
        await using var handle = await DiskBufferHandle.FromBytesAsync(payload, ".dat", CancellationToken.None);

        Assert.Equal(payload.Length, handle.Length);

        using (var reader = handle.OpenRead())
        using (var memory = new MemoryStream())
        {
            reader.CopyTo(memory);
            Assert.True(payload.SequenceEqual(memory.ToArray()));
        }

        var path = handle.FilePath;
        var directory = Path.GetDirectoryName(path)!;
        await handle.DisposeAsync();
        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task FromStream_SynchronousMaterialisation_SupportsNonSeekableStream()
    {
        var payload = Enumerable.Range(0, 2048).Select(i => (byte)(i % 241)).ToArray();
        using var source = new NonSeekableStream(payload);

        await using var handle = DiskBufferHandle.FromStream(source, extensionHint: ".sync", bufferSize: 256);
        using (var reader = handle.OpenRead())
        using (var memory = new MemoryStream())
        {
            reader.CopyTo(memory);
            Assert.True(payload.SequenceEqual(memory.ToArray()));
        }
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream inner;

        public NonSeekableStream(byte[] payload)
        {
            inner = new MemoryStream(payload, writable: false);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
