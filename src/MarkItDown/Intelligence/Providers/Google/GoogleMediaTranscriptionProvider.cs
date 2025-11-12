using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using Google.Api.Gax.Grpc;
using Google.Cloud.Speech.V1;
using Google.Protobuf.WellKnownTypes;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;

namespace MarkItDown.Intelligence.Providers.Google;

/// <summary>
/// Google Speech-to-Text implementation of <see cref="IMediaTranscriptionProvider"/>.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class GoogleMediaTranscriptionProvider : IMediaTranscriptionProvider
{
    private readonly SpeechClient _client;
    private readonly GoogleMediaIntelligenceOptions _options;

    public GoogleMediaTranscriptionProvider(GoogleMediaIntelligenceOptions options, SpeechClient? client = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (client is not null)
        {
            _client = client;
        }
        else
        {
            var builder = new SpeechClientBuilder();

            var credential = GoogleCredentialResolver.Resolve(
                options.Credential,
                options.JsonCredentials,
                options.CredentialsPath,
                SpeechClient.DefaultScopes);

            if (credential is not null)
            {
                builder.Credential = credential;
            }

            _client = builder.Build();
        }
    }

    public async Task<MediaTranscriptionResult?> TranscribeAsync(Stream stream, StreamInfo streamInfo, MediaTranscriptionRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        await using var handle = await DiskBufferHandle.FromStreamAsync(stream, streamInfo.Extension, bufferSize: 256 * 1024, onChunkWritten: null, cancellationToken).ConfigureAwait(false);
        var audioBytes = await File.ReadAllBytesAsync(handle.FilePath, cancellationToken).ConfigureAwait(false);

        var language = string.IsNullOrWhiteSpace(request?.Language) ? _options.LanguageCode : request.Language;

        var config = new RecognitionConfig
        {
            LanguageCode = language,
            EnableAutomaticPunctuation = _options.EnableAutomaticPunctuation,
            Encoding = RecognitionConfig.Types.AudioEncoding.EncodingUnspecified
        };

        if (_options.SampleRateHertz.HasValue)
        {
            config.SampleRateHertz = _options.SampleRateHertz.Value;
        }

        if (!string.IsNullOrWhiteSpace(request?.VocabularyName))
        {
            config.Adaptation = new SpeechAdaptation
            {
                PhraseSetReferences = { request.VocabularyName }
            };
        }

        var audio = RecognitionAudio.FromBytes(audioBytes);
        var callSettings = CallSettings.FromCancellationToken(cancellationToken);

        IReadOnlyList<SpeechRecognitionResult> results;
        if (audioBytes.Length < 10 * 1024 * 1024)
        {
            var response = await _client.RecognizeAsync(config, audio, callSettings).ConfigureAwait(false);
            results = response.Results;
        }
        else
        {
            var operation = await _client.LongRunningRecognizeAsync(config, audio, callSettings).ConfigureAwait(false);
            var completed = await operation.PollUntilCompletedAsync(callSettings: callSettings).ConfigureAwait(false);
            results = completed.Result.Results;
        }

        if (results.Count == 0)
        {
            return null;
        }

        var segments = new List<MediaTranscriptSegment>();
        foreach (var result in results)
        {
            var alternative = result.Alternatives.FirstOrDefault();
            if (alternative is null)
            {
                continue;
            }

            var words = alternative.Words;
            TimeSpan? start = words.Count > 0 ? ToTimeSpan(words.First().StartTime) : null;
            TimeSpan? end = words.Count > 0 ? ToTimeSpan(words.Last().EndTime) : null;

            var metadata = new Dictionary<string, string>
            {
                [MetadataKeys.Confidence] = alternative.Confidence.ToString(CultureInfo.InvariantCulture)
            };

            segments.Add(new MediaTranscriptSegment(alternative.Transcript, start, end, metadata));
        }

        if (segments.Count == 0)
        {
            return null;
        }

        var resultMetadata = new Dictionary<string, string>
        {
            [MetadataKeys.Provider] = MetadataValues.ProviderGoogleSpeechToText,
            [MetadataKeys.Language] = language
        };

        return new MediaTranscriptionResult(segments, language, resultMetadata);
    }

    private static TimeSpan? ToTimeSpan(Duration? duration)
    {
        if (duration is null)
        {
            return null;
        }

        return duration.ToTimeSpan();
    }
}
