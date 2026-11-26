using Google.Apis.Auth.OAuth2;
using MarkItDown.Intelligence.Models;

namespace MarkItDown.Intelligence.Providers.Google;

/// <summary>Explicit Google Cloud Speech-to-Text V2 and private staging settings.</summary>
public sealed class GoogleSpeechV2TranscriptionOptions
{
    /// <summary>The numeric Google Cloud project number.</summary>
    public required string ProjectId { get; init; }

    /// <summary>The Speech-to-Text location, such as eu, us or global.</summary>
    public required string Location { get; init; }

    /// <summary>The Speech-to-Text V2 model available in the selected location.</summary>
    public required string Model { get; init; }

    /// <summary>The BCP 47 language code supported by the selected model and location.</summary>
    public required string LanguageCode { get; init; }

    /// <summary>An existing private Google Cloud Storage bucket.</summary>
    public required string BucketName { get; init; }

    /// <summary>An explicit credential authorized for batch recognition and staging.</summary>
    public required GoogleCredential Credential { get; init; }
}

/// <summary>A durable reference to one immutable source staged for Google Speech V2.</summary>
public sealed record GoogleSpeechV2TranscriptionInput(
    string ProjectId,
    string Location,
    string BucketName,
    Guid CommandId,
    string SourceSha256,
    string Extension);

/// <summary>The native Google operation name and its staged input.</summary>
public sealed record GoogleSpeechV2TranscriptionOperation(string Name, GoogleSpeechV2TranscriptionInput Input);

/// <summary>The last observed state of one Google Speech V2 operation.</summary>
public enum GoogleSpeechV2TranscriptionState
{
    /// <summary>The provider accepted the recording or is processing it.</summary>
    Pending,

    /// <summary>The provider finished recognition.</summary>
    Processed,

    /// <summary>Recognition failed or returned invalid timing data.</summary>
    Failed,

    /// <summary>The provider confirmed cancellation.</summary>
    Cancelled
}

/// <summary>A Google Speech V2 observation and its timed transcript or safe error code.</summary>
public sealed record GoogleSpeechV2Transcription(
    GoogleSpeechV2TranscriptionState State,
    MediaTranscriptionResult? Transcript = null,
    string? ErrorCode = null);
