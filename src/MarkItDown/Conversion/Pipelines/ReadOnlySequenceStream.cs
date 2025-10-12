using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MarkItDown.Conversion.Pipelines;

internal sealed class ReadOnlySequenceStream : Stream
{
    private readonly ReadOnlySequence<byte> sequence;
    private long position;

    public ReadOnlySequenceStream(ReadOnlySequence<byte> sequence)
    {
        this.sequence = sequence;
        position = 0;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => sequence.Length;

    public override long Position
    {
        get => position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset + count > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (count == 0 || position >= Length)
        {
            return 0;
        }

        var remaining = (int)Math.Min(count, Length - position);
        var slice = sequence.Slice(position, remaining);
        var destination = buffer.AsSpan(offset, remaining);
        CopySlice(slice, destination);
        position += remaining;
        return remaining;
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0 || position >= Length)
        {
            return 0;
        }

        var remaining = (int)Math.Min(buffer.Length, Length - position);
        var slice = sequence.Slice(position, remaining);
        CopySlice(slice, buffer[..remaining]);
        position += remaining;
        return remaining;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = Read(buffer.Span);
        return new ValueTask<int>(count);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read(buffer, offset, count));
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (target < 0 || target > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        position = target;
        return position;
    }

    public override void SetLength(long value)
        => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    public override void Write(ReadOnlySpan<byte> buffer)
        => throw new NotSupportedException();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    private static void CopySlice(ReadOnlySequence<byte> slice, Span<byte> destination)
    {
        var remaining = destination;
        foreach (var memory in slice)
        {
            var source = memory.Span;
            if (source.Length > remaining.Length)
            {
                source = source[..remaining.Length];
            }

            source.CopyTo(remaining);
            remaining = remaining[source.Length..];
            if (remaining.Length == 0)
            {
                break;
            }
        }
    }
}
