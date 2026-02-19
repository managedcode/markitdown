using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MarkItDown;

/// <summary>
/// Wraps a disk-backed copy of a stream or byte payload so converters can reopen read-only streams without relying on <see cref="MemoryStream"/>.
/// </summary>
internal sealed class DiskBufferHandle : IAsyncDisposable
{
    private readonly string directoryPath;
    private readonly string filePath;
    private readonly long length;
    private int disposeState;

    private DiskBufferHandle(string directoryPath, string filePath, long length)
    {
        this.directoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
        this.filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        this.length = length;
    }

    /// <summary>
    /// Gets the absolute file path where the payload is stored.
    /// </summary>
    public string FilePath => filePath;

    /// <summary>
    /// Gets the length in bytes of the buffered payload.
    /// </summary>
    public long Length => length;

    /// <summary>
    /// Materialise the provided <paramref name="source"/> stream to disk.
    /// </summary>
    /// <param name="source">Source stream to buffer.</param>
    /// <param name="extensionHint">Optional extension (with or without dot) used for the backing file.</param>
    /// <param name="bufferSize">Preferred buffer size when copying.</param>
    /// <param name="onChunkWritten">Optional callback invoked after each chunk is written (argument is total bytes written so far).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async ValueTask<DiskBufferHandle> FromStreamAsync(
        Stream source,
        string? extensionHint,
        int bufferSize,
        Action<long>? onChunkWritten,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var (directory, path) = CreateWorkspace(extensionHint);
        var options = new FileStreamOptions
        {
            Access = FileAccess.Write,
            Mode = FileMode.Create,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };

        long total = 0;
        var rented = ArrayPool<byte>.Shared.Rent(Math.Max(bufferSize, 4096));
        try
        {
            await using (var writer = new FileStream(path, options))
            {
                while (true)
                {
                    var read = await source.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    await writer.WriteAsync(rented.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    total += read;
                    onChunkWritten?.Invoke(total);
                }

                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            TryDeleteFile(path);
            TryDeleteDirectory(directory);
            throw;
        }

        ArrayPool<byte>.Shared.Return(rented, clearArray: true);

        if (source.CanSeek)
        {
            try
            {
                source.Position = 0;
            }
            catch
            {
                // Ignore seek failures on partially seekable streams.
            }
        }

        return new DiskBufferHandle(directory, path, total);
    }

    /// <summary>
    /// Materialise the provided <paramref name="source"/> stream to disk using synchronous IO.
    /// </summary>
    public static DiskBufferHandle FromStream(
        Stream source,
        string? extensionHint,
        int bufferSize)
    {
        ArgumentNullException.ThrowIfNull(source);

        var (directory, path) = CreateWorkspace(extensionHint);
        var options = new FileStreamOptions
        {
            Access = FileAccess.Write,
            Mode = FileMode.Create,
            Share = FileShare.None,
            Options = FileOptions.SequentialScan
        };

        long total = 0;
        var rented = ArrayPool<byte>.Shared.Rent(Math.Max(bufferSize, 4096));
        try
        {
            using (var writer = new FileStream(path, options))
            {
                while (true)
                {
                    var read = source.Read(rented, 0, rented.Length);
                    if (read == 0)
                    {
                        break;
                    }

                    writer.Write(rented, 0, read);
                    total += read;
                }

                writer.Flush();
            }
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            TryDeleteFile(path);
            TryDeleteDirectory(directory);
            throw;
        }

        ArrayPool<byte>.Shared.Return(rented, clearArray: true);

        if (source.CanSeek)
        {
            try
            {
                source.Position = 0;
            }
            catch
            {
            }
        }

        return new DiskBufferHandle(directory, path, total);
    }

    /// <summary>
    /// Persist the provided byte payload to disk and return a handle that can reopen it as a stream.
    /// </summary>
    public static async ValueTask<DiskBufferHandle> FromBytesAsync(ReadOnlyMemory<byte> payload, string? extensionHint, CancellationToken cancellationToken)
    {
        var (directory, path) = CreateWorkspace(extensionHint);

        try
        {
            var options = new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.Create,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            };

            await using var writer = new FileStream(path, options);
            await writer.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            return new DiskBufferHandle(directory, path, payload.Length);
        }
        catch
        {
            TryDeleteFile(path);
            TryDeleteDirectory(directory);
            throw;
        }
    }

    /// <summary>
    /// Reopen the buffered payload as a read-only stream.
    /// </summary>
    public Stream OpenRead()
    {
        EnsureNotDisposed();

        var options = new FileStreamOptions
        {
            Access = FileAccess.Read,
            Mode = FileMode.Open,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };

        return new FileStream(filePath, options);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
        {
            return;
        }

        await Task.Run(() =>
        {
            TryDeleteFile(filePath);
            TryDeleteDirectory(directoryPath);
        }).ConfigureAwait(false);
    }

    private void EnsureNotDisposed()
    {
        if (disposeState != 0)
        {
            throw new ObjectDisposedException(nameof(DiskBufferHandle));
        }
    }

    private static (string Directory, string Path) CreateWorkspace(string? extensionHint)
    {
        var root = MarkItDownPathResolver.Ensure("buffers", Guid.NewGuid().ToString("N"));

        var extension = NormalizeExtension(extensionHint);
        var filePath = Path.Combine(root, $"payload{extension}");
        return (root, filePath);
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return ".bin";
        }

        return extension.StartsWith('.')
            ? extension
            : "." + extension;
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
