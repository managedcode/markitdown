using System.Globalization;
using Google.Api.Gax.Grpc;
using Google.Cloud.Speech.V2;
using Google.LongRunning;
using Grpc.Core;
using ManagedCode.Storage.Core;
using ManagedCode.Storage.Google;
using ManagedCode.Storage.Google.Options;
using MarkItDown.Intelligence.Models;

namespace MarkItDown.Intelligence.Providers.Google;

/// <summary>Stages private recordings and runs resumable Google Cloud Speech-to-Text V2 batch recognition.</summary>
public sealed class GoogleSpeechV2TranscriptionProvider
{
    private const string StagingDirectory = "markitdown-transcription";
    private static readonly RetrySettings SingleSubmission = RetrySettings.FromConstantBackoff(
        1, TimeSpan.Zero, static _ => false);

    private readonly GoogleSpeechV2TranscriptionOptions _options;
    private readonly SpeechClient _client;
    private readonly Func<IStorage> _createStorage;

    /// <summary>Creates a provider for the specified Google Cloud project, region and private bucket.</summary>
    public GoogleSpeechV2TranscriptionProvider(GoogleSpeechV2TranscriptionOptions options)
        : this(options, CreateClient(options), () => CreateStorage(options))
    {
    }

    internal GoogleSpeechV2TranscriptionProvider(
        GoogleSpeechV2TranscriptionOptions options,
        SpeechClient client,
        Func<IStorage> createStorage)
    {
        _options = options;
        _client = client;
        _createStorage = createStorage;
    }

    /// <summary>Builds a stable input reference before transferring any bytes.</summary>
    /// <param name="commandId">Stable ID of the accepted recognition command.</param>
    /// <param name="sourceSha256">Verified SHA-256 of the exact immutable source version.</param>
    /// <param name="streamInfo">Audio or video container metadata.</param>
    public GoogleSpeechV2TranscriptionInput CreateInputReference(
        Guid commandId,
        string sourceSha256,
        StreamInfo streamInfo)
    {
        ArgumentNullException.ThrowIfNull(streamInfo);
        if (commandId == Guid.Empty)
        {
            throw new ArgumentException("A stable command ID is required.", nameof(commandId));
        }

        if (sourceSha256 is not { Length: 64 } || !sourceSha256.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("A verified SHA-256 source identity is required.", nameof(sourceSha256));
        }

        var extension = GetSupportedExtension(streamInfo);
        return new GoogleSpeechV2TranscriptionInput(
            _options.ProjectId,
            _options.Location,
            _options.BucketName,
            commandId,
            sourceSha256.ToLowerInvariant(),
            extension);
    }

    /// <summary>Uploads the immutable source to the exact staged input. Repeat only with the same verified source.</summary>
    public async Task StageAsync(
        GoogleSpeechV2TranscriptionInput input,
        Stream stream,
        StreamInfo streamInfo,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(input);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || stream.CanSeek && (stream.Position != 0 || stream.Length == 0))
        {
            throw new ArgumentException("The readable, non-empty original recording must be supplied from its beginning.", nameof(stream));
        }

        if (GetSupportedExtension(streamInfo) != input.Extension)
        {
            throw new ArgumentException("The recording container differs from the staged reference.", nameof(streamInfo));
        }

        using var storage = _createStorage();
        var uploaded = await storage.UploadAsync(stream, settings =>
        {
            settings.Directory = StagingDirectory;
            settings.FileName = GetStagedFileName(input);
            settings.MimeType = streamInfo.ResolveMimeType();
        }, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        if (uploaded.IsFailed || uploaded.Value.Length == 0)
        {
            throw new InvalidOperationException("Google Cloud Storage did not stage a non-empty recording.");
        }
    }

    /// <summary>Submits the staged input once and returns the Google operation name for durable storage.</summary>
    public async Task<GoogleSpeechV2TranscriptionOperation> StartAsync(
        GoogleSpeechV2TranscriptionInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(input);
        var request = new BatchRecognizeRequest
        {
            RecognizerAsRecognizerName = RecognizerName.FromProjectLocationRecognizer(
                _options.ProjectId, _options.Location, "_"),
            Config = new RecognitionConfig
            {
                Model = _options.Model,
                LanguageCodes = { _options.LanguageCode },
                AutoDecodingConfig = new AutoDetectDecodingConfig(),
                Features = new RecognitionFeatures { EnableWordTimeOffsets = true }
            },
            Files = { new BatchRecognizeFileMetadata { Uri = GetStagedUri(input) } },
            RecognitionOutputConfig = new RecognitionOutputConfig
            {
                InlineResponseConfig = new InlineOutputConfig()
            }
        };
        var settings = CallSettings.FromCancellationToken(cancellationToken).WithRetry(SingleSubmission);
        var accepted = await _client.BatchRecognizeAsync(request, settings).ConfigureAwait(false);
        return new GoogleSpeechV2TranscriptionOperation(accepted.Name, input);
    }

    /// <summary>Recovers an accepted operation by its exact staged input when the submission response was lost.</summary>
    /// <remarks>A missing operation can be temporarily invisible. It does not make another submission safe.</remarks>
    public async Task<GoogleSpeechV2TranscriptionOperation?> FindOperationAsync(
        GoogleSpeechV2TranscriptionInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(input);
        var stagedUri = GetStagedUri(input);
        var request = new ListOperationsRequest
        {
            Name = $"projects/{_options.ProjectId}/locations/{_options.Location}",
            Filter = "is_v2(name)"
        };
        var operations = _client.BatchRecognizeOperationsClient.ListOperationsAsync(
            request, CallSettings.FromCancellationToken(cancellationToken));

        string? match = null;
        await foreach (var operation in operations.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!IsOperationNameInScope(operation.Name) ||
                operation.Metadata is null ||
                !operation.Metadata.TryUnpack<OperationMetadata>(out var metadata) ||
                metadata.BatchRecognizeRequest?.Files is not { Count: 1 } files ||
                !string.Equals(files[0].Uri, stagedUri, StringComparison.Ordinal))
            {
                continue;
            }

            if (match is not null && !string.Equals(match, operation.Name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Multiple Google Speech operations match the exact staged recording.");
            }

            match = operation.Name;
        }

        return match is null ? null : new GoogleSpeechV2TranscriptionOperation(match, input);
    }

    /// <summary>Reads one observation of the stored Google operation.</summary>
    public async Task<GoogleSpeechV2Transcription> ReadAsync(
        GoogleSpeechV2TranscriptionOperation operation,
        CancellationToken cancellationToken = default)
    {
        ValidateOperation(operation);
        var response = await _client.BatchRecognizeOperationsClient
            .GetOperationAsync(operation.Name, cancellationToken).ConfigureAwait(false);
        if (!response.Done)
        {
            return new GoogleSpeechV2Transcription(GoogleSpeechV2TranscriptionState.Pending);
        }

        if (response.Error is not null)
        {
            return MapError(response.Error.Code);
        }

        if (response.Response is null ||
            !response.Response.TryUnpack<BatchRecognizeResponse>(out var result) ||
            !result.Results.TryGetValue(GetStagedUri(operation.Input), out var file))
        {
            return Failure("missing-batch-result");
        }

        return file.Error is { Code: not 0 }
            ? MapError(file.Error.Code)
            : BuildTranscript(file.InlineResult?.Transcript);
    }

    /// <summary>Requests cancellation. The caller must observe a terminal state before deleting the source.</summary>
    public Task CancelAsync(
        GoogleSpeechV2TranscriptionOperation operation,
        CancellationToken cancellationToken = default)
    {
        ValidateOperation(operation);
        return _client.BatchRecognizeOperationsClient.CancelOperationAsync(operation.Name, cancellationToken);
    }

    /// <summary>Deletes the staged input after terminal processing or when submission did not occur.</summary>
    public async Task DeleteStagedAsync(
        GoogleSpeechV2TranscriptionInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(input);
        using var storage = _createStorage();
        var result = await storage.DeleteAsync(settings =>
        {
            settings.Directory = StagingDirectory;
            settings.FileName = GetStagedFileName(input);
        }, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.IsFailed)
        {
            throw new InvalidOperationException("Google Cloud Storage did not remove the staged recording.");
        }
    }

    private static SpeechClient CreateClient(GoogleSpeechV2TranscriptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Credential);
        if (string.IsNullOrWhiteSpace(options.ProjectId) || !options.ProjectId.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Google Speech V2 requires the numeric project number.", nameof(options));
        }

        if (!IsResourceLabel(options.Location) || !IsResourceLabel(options.BucketName, allowBucketPunctuation: true))
        {
            throw new ArgumentException("An explicit Google Speech location and private GCS bucket are required.", nameof(options));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(options.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LanguageCode);
        return new SpeechClientBuilder
        {
            Endpoint = options.Location == "global" ? SpeechClient.DefaultEndpoint : $"{options.Location}-speech.googleapis.com:443",
            Credential = options.Credential
        }.Build();
    }

    private static IStorage CreateStorage(GoogleSpeechV2TranscriptionOptions options) =>
        new GCPStorage(new GCPStorageOptions
        {
            GoogleCredential = options.Credential,
            CreateContainerIfNotExists = false,
            BucketOptions = new BucketOptions { Bucket = options.BucketName, ProjectId = options.ProjectId }
        });

    private MediaTranscriptionResult CreateTranscript(List<MediaTranscriptSegment> segments) =>
        new(segments, _options.LanguageCode, new Dictionary<string, string>
        {
            [MetadataKeys.Provider] = MetadataValues.ProviderGoogleSpeechToText,
            [MetadataKeys.Language] = _options.LanguageCode
        });

    private GoogleSpeechV2Transcription BuildTranscript(BatchRecognizeResults? transcript)
    {
        if (transcript is null)
        {
            return Failure("missing-transcript");
        }

        var segments = new List<MediaTranscriptSegment>();
        foreach (var result in transcript.Results)
        {
            var alternative = result.Alternatives.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(alternative?.Transcript))
            {
                continue;
            }

            var start = alternative.Words.FirstOrDefault()?.StartOffset?.ToTimeSpan();
            var end = alternative.Words.LastOrDefault()?.EndOffset?.ToTimeSpan();
            if (start is not { Ticks: >= 0 } || end is null || end <= start)
            {
                return Failure("missing-word-offsets");
            }

            segments.Add(new MediaTranscriptSegment(alternative.Transcript, start, end, new Dictionary<string, string>
            {
                [MetadataKeys.Confidence] = alternative.Confidence.ToString(CultureInfo.InvariantCulture)
            }));
        }

        return new GoogleSpeechV2Transcription(GoogleSpeechV2TranscriptionState.Processed, CreateTranscript(segments));
    }

    private void ValidateInput(GoogleSpeechV2TranscriptionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var expected = CreateInputReference(input.CommandId, input.SourceSha256, new StreamInfo(extension: input.Extension));
        if (input != expected)
        {
            throw new ArgumentException("The staged input belongs to a different Google Speech account or region.", nameof(input));
        }
    }

    private void ValidateOperation(GoogleSpeechV2TranscriptionOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidateInput(operation.Input);
        if (!IsOperationNameInScope(operation.Name))
        {
            throw new ArgumentException("The operation name is outside the configured Google Speech account or region.", nameof(operation));
        }
    }

    private bool IsOperationNameInScope(string? name)
    {
        var prefix = $"projects/{_options.ProjectId}/locations/{_options.Location}/operations/v2-";
        return name is not null && name.Length > prefix.Length &&
               name.StartsWith(prefix, StringComparison.Ordinal) &&
               !name.AsSpan(prefix.Length).Contains('/');
    }

    private static GoogleSpeechV2Transcription MapError(int code) =>
        new(code == (int)StatusCode.Cancelled
                ? GoogleSpeechV2TranscriptionState.Cancelled
                : GoogleSpeechV2TranscriptionState.Failed,
            ErrorCode: $"google-rpc-{code.ToString(CultureInfo.InvariantCulture)}");

    private static GoogleSpeechV2Transcription Failure(string code) =>
        new(GoogleSpeechV2TranscriptionState.Failed, ErrorCode: code);

    private static bool IsResourceLabel(string value, bool allowBucketPunctuation = false) =>
        !string.IsNullOrWhiteSpace(value) && value.All(c =>
            char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-' || allowBucketPunctuation && c is '.' or '_');

    private static string GetSupportedExtension(StreamInfo streamInfo)
    {
        ArgumentNullException.ThrowIfNull(streamInfo);
        var extension = (streamInfo.Extension ?? Path.GetExtension(streamInfo.FileName))?.ToLowerInvariant();
        return extension is ".wav" or ".flac" or ".mp3" or ".ogg" or ".opus" or ".webm" or ".mp4" or ".m4a" or ".mov"
            ? extension
            : throw new NotSupportedException("The recording container is unsupported by Google Speech V2 batch recognition.");
    }

    private static string GetStagedFileName(GoogleSpeechV2TranscriptionInput input) =>
        $"{input.CommandId:N}-{input.SourceSha256}{input.Extension}";

    private static string GetStagedUri(GoogleSpeechV2TranscriptionInput input) =>
        $"gs://{input.BucketName}/{StagingDirectory}/{GetStagedFileName(input)}";
}
