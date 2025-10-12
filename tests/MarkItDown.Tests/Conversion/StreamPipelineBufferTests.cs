using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;
using MarkItDown.Conversion.Pipelines;
using Xunit;

namespace MarkItDown.Tests.Conversion;

public class StreamPipelineBufferTests
{
    [Fact]
    public async Task CreateAsync_NonSeekableStream_AllowsMultipleReaders()
    {
        var payload = Enumerable.Range(0, 4096).Select(i => (byte)(i % 255)).ToArray();
        await using var source = new NonSeekableStream(payload);

        await using var buffer = await StreamPipelineBuffer.CreateAsync(source, segmentSize: 256, progress: null, ProgressDetailLevel.Detailed, CancellationToken.None);

        Assert.Equal(payload.Length, buffer.Length);

        using var first = buffer.CreateStream();
        using var second = buffer.CreateStream();

        Assert.Equal(payload, ReadAll(first));
        Assert.Equal(payload, ReadAll(second));
    }

    [Fact]
    public async Task CreateAsync_ReportsProgress_WhenDetailLevelIsDetailed()
    {
        var payload = Enumerable.Range(0, 1024).Select(i => (byte)(i % 127)).ToArray();
        await using var source = new NonSeekableStream(payload);

        var collector = new ProgressCollector();
        await using var buffer = await StreamPipelineBuffer.CreateAsync(source, segmentSize: 128, progress: collector, ProgressDetailLevel.Detailed, CancellationToken.None);

        Assert.Equal(payload.Length, buffer.Length);
        Assert.Contains(collector.Items, item => item.Stage == "buffer-stream");
        Assert.Contains(collector.Items, item => item.Stage == "buffered");
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private sealed class ProgressCollector : IProgress<ConversionProgress>
    {
        private readonly List<ConversionProgress> items = new();

        public IReadOnlyList<ConversionProgress> Items => items;

        public void Report(ConversionProgress value) => items.Add(value);
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly Stream inner;

        public NonSeekableStream(IReadOnlyList<byte> data)
        {
            inner = new MemoryStream(data.ToArray());
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
