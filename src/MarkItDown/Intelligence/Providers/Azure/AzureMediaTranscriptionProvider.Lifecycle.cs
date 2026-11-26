using MarkItDown.Intelligence.Providers.Azure.VideoIndexer;

namespace MarkItDown.Intelligence.Providers.Azure;

public sealed partial class AzureMediaTranscriptionProvider
{
    /// <summary>
    /// Submits private speech-only indexing of a recording and returns the Video Indexer ID.
    /// </summary>
    /// <remarks>
    /// Persist <paramref name="externalId"/> before submitting. After an uncertain response,
    /// use <see cref="FindVideoIdAsync"/> to recover the assigned ID. This method sends one
    /// upload request. Use an HTTP transport that does not replay or redirect uploads.
    /// </remarks>
    /// <param name="stream">The caller-owned original recording stream.</param>
    /// <param name="streamInfo">The original recording's format metadata.</param>
    /// <param name="externalId">A stable opaque identifier with ASCII letters, digits, dashes, or underscores.</param>
    /// <param name="sourceLanguage">The Azure Video Indexer language code of the original speech.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<string> SubmitAsync(
        Stream stream, StreamInfo streamInfo, string externalId, string sourceLanguage,
        CancellationToken cancellationToken = default) =>
        _client.SubmitAsync(stream, streamInfo, externalId, sourceLanguage, cancellationToken);

    /// <summary>
    /// Reads the Video Indexer ID for a persisted external ID. Returns null when no exact match is visible.
    /// </summary>
    /// <remarks>
    /// Search visibility can lag behind upload. An empty result does not establish that an uncertain
    /// upload was unsuccessful. Multiple matches fail instead of selecting an arbitrary recording.
    /// </remarks>
    public Task<string?> FindVideoIdAsync(string externalId, CancellationToken cancellationToken = default) =>
        _client.FindVideoIdAsync(externalId, cancellationToken);

    /// <summary>
    /// Reads one current job state. A processed job returns its timed, original-language transcript.
    /// </summary>
    public async Task<AzureVideoIndexerTranscription> ReadAsync(string videoId, CancellationToken cancellationToken = default)
    {
        using var index = await _client.GetJobIndexAsync(videoId, cancellationToken).ConfigureAwait(false);
        var root = index.RootElement;
        var state = VideoIndexerClient.ReadJobState(root);
        var transcript = state == AzureVideoIndexerTranscriptionState.Processed
            ? ParseTranscript(root, videoId, request: null)
            : null;
        return new AzureVideoIndexerTranscription(state, transcript);
    }

    /// <summary>
    /// Deletes the exact private provider recording and its insights. An already removed ID is successful.
    /// </summary>
    public Task DeleteAsync(string videoId, CancellationToken cancellationToken = default) =>
        _client.DeleteAsync(videoId, cancellationToken);
}
