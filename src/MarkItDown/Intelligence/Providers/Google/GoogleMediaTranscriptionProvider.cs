using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using Google.Api.Gax.Grpc;
using Google.Cloud.Speech.V1;
using Google.Protobuf;
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
    private const long MaximumInlineAudioBytes = 10_000_000;
    private const int BufferSize = 256 * 1024;

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
        ArgumentNullException.ThrowIfNull(stream);

        if (stream.CanSeek)
        {
            EnsureInlineSize(stream.Length - stream.Position);
        }

        await using var handle = await DiskBufferHandle.FromStreamAsync(stream, streamInfo.Extension,
            BufferSize, EnsureInlineSize, cancellationToken).ConfigureAwait(false);
        if (handle.Length == 0)
        {
            throw new ArgumentException("Google Speech-to-Text requires a non-empty audio file.", nameof(stream));
        }

        var language = string.IsNullOrWhiteSpace(request?.Language) ? _options.LanguageCode : request.Language;

        var config = new RecognitionConfig
        {
            LanguageCode = language,
            EnableAutomaticPunctuation = _options.EnableAutomaticPunctuation,
            EnableWordTimeOffsets = true,
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

        await using var bufferedAudio = handle.OpenRead();
        var audio = new RecognitionAudio
        {
            Content = await ByteString.FromStreamAsync(bufferedAudio, cancellationToken).ConfigureAwait(false)
        };
        var callSettings = CallSettings.FromCancellationToken(cancellationToken);
        var response = await _client.RecognizeAsync(config, audio, callSettings).ConfigureAwait(false);
        var results = response.Results;

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

    private static void EnsureInlineSize(long bytes)
    {
        if (bytes > MaximumInlineAudioBytes)
        {
            throw new InvalidOperationException(
                "Google Speech-to-Text inline recognition accepts up to 10 MB and 60 seconds. Longer recordings require Google Cloud Storage batch transcription.");
        }
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
