using MarkItDown.Intelligence.Models;

namespace MarkItDown.Intelligence.Providers.Azure;

/// <summary>Current processing state of one Azure Video Indexer recording.</summary>
public enum AzureVideoIndexerTranscriptionState
{
    /// <summary>The provider accepted the recording or is processing it.</summary>
    Pending,

    /// <summary>The provider finished and the original-language transcript can be read.</summary>
    Processed,

    /// <summary>The provider failed or quarantined the recording.</summary>
    Failed
}

/// <summary>One observed provider state and its timed transcript, when available.</summary>
public sealed record AzureVideoIndexerTranscription(
    AzureVideoIndexerTranscriptionState State,
    MediaTranscriptionResult? Transcript);
