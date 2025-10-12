using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MarkItDown.Conversion.Pipelines;

/// <summary>
/// Buffers a source stream to disk so downstream converters can obtain rewindable read-only streams without keeping the payload in memory.
/// </summary>
internal sealed class StreamPipelineBuffer : IAsyncDisposable
{
    private readonly DiskBufferHandle handle;

    private StreamPipelineBuffer(DiskBufferHandle handle)
    {
        this.handle = handle ?? throw new ArgumentNullException(nameof(handle));
    }

    /// <summary>
    /// Gets the length of the buffered payload.
    /// </summary>
    public long Length => handle.Length;

    /// <summary>
    /// Persist the <paramref name="source"/> to disk and return a buffer that can create rewindable streams.
    /// </summary>
    public static async ValueTask<StreamPipelineBuffer> CreateAsync(
        Stream source,
        int segmentSize,
        IProgress<ConversionProgress>? progress,
        ProgressDetailLevel detailLevel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var detailed = detailLevel == ProgressDetailLevel.Detailed;
        var handle = await DiskBufferHandle.FromStreamAsync(
            source,
            extensionHint: GetExtensionHint(source),
            bufferSize: segmentSize,
            onChunkWritten: total =>
            {
                if (detailed && progress is not null)
                {
                    var completed = (int)Math.Min(total, int.MaxValue);
                    progress.Report(new ConversionProgress("buffer-stream", completed, completed, $"bytes={total}"));
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (progress is not null)
        {
            var bytes = (int)Math.Min(handle.Length, int.MaxValue);
            progress.Report(new ConversionProgress("buffered", bytes, bytes, $"bytes={handle.Length}"));
        }

        return new StreamPipelineBuffer(handle);
    }

    /// <summary>
    /// Create a new read-only stream positioned at the start of the buffered payload.
    /// </summary>
    public Stream CreateStream() => handle.OpenRead();

    public ValueTask DisposeAsync() => handle.DisposeAsync();

    private static string? GetExtensionHint(Stream source)
    {
        if (source is FileStream fileStream &&
            !string.IsNullOrWhiteSpace(fileStream.Name))
        {
            return Path.GetExtension(fileStream.Name);
        }

        return null;
    }
}
