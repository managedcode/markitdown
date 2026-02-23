namespace MarkItDown.YouTube;

internal sealed class ResolvedVideoMedia : IAsyncDisposable
{
    private readonly IAsyncDisposable? cleanupHandle;

    public ResolvedVideoMedia(string filePath, StreamInfo streamInfo, IAsyncDisposable? cleanupHandle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
        StreamInfo = streamInfo ?? throw new ArgumentNullException(nameof(streamInfo));
        this.cleanupHandle = cleanupHandle;
    }

    public string FilePath { get; }

    public StreamInfo StreamInfo { get; }

    public ValueTask DisposeAsync() => cleanupHandle?.DisposeAsync() ?? ValueTask.CompletedTask;
}
