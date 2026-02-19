using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.TranscribeService;
using Amazon.TranscribeService.Model;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;

namespace MarkItDown.Intelligence.Providers.Aws;

/// <summary>
/// AWS Transcribe implementation of <see cref="IMediaTranscriptionProvider"/>.
/// Uploads media to S3, runs a transcription job, and parses the transcript JSON.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class AwsMediaTranscriptionProvider : IMediaTranscriptionProvider, IDisposable
{
    private readonly IAmazonTranscribeService _transcribe;
    private readonly IAmazonS3 _s3;
    private readonly AwsMediaIntelligenceOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public AwsMediaTranscriptionProvider(
        AwsMediaIntelligenceOptions options,
        IAmazonTranscribeService? transcribeClient = null,
        IAmazonS3? s3Client = null,
        HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.InputBucketName))
        {
            throw new ArgumentException("AWS Transcribe requires InputBucketName to upload media.", nameof(options));
        }

        _transcribe = transcribeClient ?? CreateTranscribeClient(options);
        _s3 = s3Client ?? CreateS3Client(options);

        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }
    }

    public async Task<MediaTranscriptionResult?> TranscribeAsync(Stream stream, StreamInfo streamInfo, MediaTranscriptionRequest? request = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var jobName = $"markitdown-{Guid.NewGuid():N}";
        var extension = string.IsNullOrWhiteSpace(streamInfo.Extension) ? ".wav" : streamInfo.Extension;
        var keyPrefix = string.IsNullOrWhiteSpace(_options.InputKeyPrefix) ? string.Empty : EnsureSuffix(_options.InputKeyPrefix!, "/");
        var objectKey = keyPrefix + jobName + extension;

        var language = string.IsNullOrWhiteSpace(request?.Language) ? _options.LanguageCode : request.Language;

        await using var payloadHandle = await DiskBufferHandle.FromStreamAsync(stream, streamInfo.Extension, bufferSize: 256 * 1024, onChunkWritten: null, cancellationToken).ConfigureAwait(false);

        await UploadAsync(objectKey, payloadHandle.FilePath, streamInfo, cancellationToken).ConfigureAwait(false);

        var mediaUri = $"s3://{_options.InputBucketName}/{objectKey}";
        var startRequest = new StartTranscriptionJobRequest
        {
            TranscriptionJobName = jobName,
            LanguageCode = language,
            Media = new Media { MediaFileUri = mediaUri },
            MediaFormat = DetermineMediaFormat(extension),
            OutputBucketName = _options.OutputBucketName,
            OutputKey = BuildOutputKey(jobName)
        };

        if (!string.IsNullOrWhiteSpace(request?.VocabularyName))
        {
            startRequest.Settings = new Settings
            {
                VocabularyName = request.VocabularyName
            };
        }

        await _transcribe.StartTranscriptionJobAsync(startRequest, cancellationToken).ConfigureAwait(false);

        var job = await WaitForCompletionAsync(jobName, cancellationToken).ConfigureAwait(false);
        if (job.TranscriptionJobStatus == TranscriptionJobStatus.FAILED)
        {
            return null;
        }

        var transcriptUri = job.Transcript?.TranscriptFileUri;
        if (string.IsNullOrWhiteSpace(transcriptUri))
        {
            return null;
        }

        var transcriptJson = await _httpClient.GetStringAsync(transcriptUri, cancellationToken).ConfigureAwait(false);
        var result = ParseTranscript(transcriptJson, language);
        if (result is null)
        {
            return null;
        }

        if (_options.DeleteInputOnCompletion)
        {
            await DeleteObjectIfExistsAsync(objectKey, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task UploadAsync(string key, string filePath, StreamInfo streamInfo, CancellationToken cancellationToken)
    {
        await using var uploadStream = new FileStream(filePath, new FileStreamOptions
        {
            Access = FileAccess.Read,
            Mode = FileMode.Open,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        var request = new PutObjectRequest
        {
            BucketName = _options.InputBucketName,
            Key = key,
            InputStream = uploadStream,
            ContentType = streamInfo.MimeType ?? "application/octet-stream"
        };

        await _s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteObjectIfExistsAsync(string key, CancellationToken cancellationToken)
    {
        var deleteRequest = new DeleteObjectRequest
        {
            BucketName = _options.InputBucketName,
            Key = key
        };

        await _s3.DeleteObjectAsync(deleteRequest, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TranscriptionJob> WaitForCompletionAsync(string jobName, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await _transcribe.GetTranscriptionJobAsync(new GetTranscriptionJobRequest
            {
                TranscriptionJobName = jobName
            }, cancellationToken).ConfigureAwait(false);

            var job = response.TranscriptionJob;
            if (job.TranscriptionJobStatus == TranscriptionJobStatus.COMPLETED ||
                job.TranscriptionJobStatus == TranscriptionJobStatus.FAILED)
            {
                return job;
            }

            await Task.Delay(_options.PollingInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static MediaFormat DetermineMediaFormat(string extension)
    {
        var normalized = string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.ToLowerInvariant();

        if (normalized == ".mp3")
        {
            return MediaFormat.Mp3;
        }

        if (normalized == ".mp4")
        {
            return MediaFormat.Mp4;
        }

        if (normalized == ".flac")
        {
            return MediaFormat.Flac;
        }

        if (normalized == ".ogg")
        {
            return MediaFormat.Ogg;
        }

        if (normalized == ".amr")
        {
            return MediaFormat.Amr;
        }

        if (normalized == ".webm")
        {
            return MediaFormat.Webm;
        }

        return MediaFormat.Wav;
    }

    private string? BuildOutputKey(string jobName)
    {
        if (string.IsNullOrWhiteSpace(_options.OutputBucketName))
        {
            return null;
        }

        var prefix = string.IsNullOrWhiteSpace(_options.OutputKeyPrefix) ? string.Empty : EnsureSuffix(_options.OutputKeyPrefix!, "/");
        return prefix + jobName + ".json";
    }

    private static string EnsureSuffix(string value, string suffix)
    {
        return value.EndsWith(suffix, StringComparison.Ordinal) ? value : value + suffix;
    }

    private static MediaTranscriptionResult? ParseTranscript(string json, string language)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("results", out var results))
        {
            return null;
        }

        var metadata = new Dictionary<string, string>
        {
            [MetadataKeys.Provider] = MetadataValues.ProviderAwsTranscribe
        };

        var segments = new List<MediaTranscriptSegment>();
        if (results.TryGetProperty("items", out var items))
        {
            segments.AddRange(BuildSegmentsFromItems(items));
        }

        if (segments.Count == 0)
        {
            var transcripts = results.TryGetProperty("transcripts", out var transcriptArray) && transcriptArray.GetArrayLength() > 0
                ? transcriptArray[0].GetProperty("transcript").GetString()
                : string.Empty;

            segments.Add(new MediaTranscriptSegment(transcripts ?? string.Empty));
        }

        metadata[MetadataKeys.Language] = language;

        return new MediaTranscriptionResult(segments, language, metadata);
    }

    private static IEnumerable<MediaTranscriptSegment> BuildSegmentsFromItems(JsonElement items)
    {
        var segments = new List<MediaTranscriptSegment>();
        if (items.ValueKind != JsonValueKind.Array)
        {
            return segments;
        }

        var buffer = new StringBuilder();
        double? start = null;
        double? end = null;

        foreach (var item in items.EnumerateArray())
        {
            var type = item.GetProperty("type").GetString();
            var alternatives = item.GetProperty("alternatives");
            if (alternatives.GetArrayLength() == 0)
            {
                continue;
            }

            var content = alternatives[0].GetProperty("content").GetString() ?? string.Empty;

            if (string.Equals(type, "pronunciation", StringComparison.OrdinalIgnoreCase))
            {
                start ??= ParseTimeSeconds(item.GetProperty("start_time"));
                end = ParseTimeSeconds(item.GetProperty("end_time"));
                if (buffer.Length > 0)
                {
                    buffer.Append(' ');
                }
                buffer.Append(content);
            }
            else if (string.Equals(type, "punctuation", StringComparison.OrdinalIgnoreCase))
            {
                buffer.Append(content);
                if (buffer.Length > 0)
                {
                    segments.Add(new MediaTranscriptSegment(
                        buffer.ToString().Trim(),
                        start.HasValue ? TimeSpan.FromSeconds(start.Value) : null,
                        end.HasValue ? TimeSpan.FromSeconds(end.Value) : null));
                }

                buffer.Clear();
                start = null;
                end = null;
            }
        }

        if (buffer.Length > 0)
        {
            segments.Add(new MediaTranscriptSegment(
                buffer.ToString().Trim(),
                start.HasValue ? TimeSpan.FromSeconds(start.Value) : null,
                end.HasValue ? TimeSpan.FromSeconds(end.Value) : null));
        }

        return segments;
    }

    private static double? ParseTimeSeconds(JsonElement element)
    {
        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds;
        }

        return null;
    }

    private static IAmazonTranscribeService CreateTranscribeClient(AwsMediaIntelligenceOptions options)
    {
        var region = ResolveRegion(options.Region);

        if (options.Credentials is not null)
        {
            return new AmazonTranscribeServiceClient(options.Credentials, region);
        }

        if (!string.IsNullOrWhiteSpace(options.AccessKeyId) && !string.IsNullOrWhiteSpace(options.SecretAccessKey))
        {
            AWSCredentials credentials = string.IsNullOrWhiteSpace(options.SessionToken)
                ? new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey)
                : new SessionAWSCredentials(options.AccessKeyId, options.SecretAccessKey, options.SessionToken);

            return new AmazonTranscribeServiceClient(credentials, region);
        }

        return new AmazonTranscribeServiceClient(region);
    }

    private static IAmazonS3 CreateS3Client(AwsMediaIntelligenceOptions options)
    {
        var region = ResolveRegion(options.Region);

        if (options.Credentials is not null)
        {
            return new AmazonS3Client(options.Credentials, region);
        }

        if (!string.IsNullOrWhiteSpace(options.AccessKeyId) && !string.IsNullOrWhiteSpace(options.SecretAccessKey))
        {
            AWSCredentials credentials = string.IsNullOrWhiteSpace(options.SessionToken)
                ? new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey)
                : new SessionAWSCredentials(options.AccessKeyId, options.SecretAccessKey, options.SessionToken);

            return new AmazonS3Client(credentials, region);
        }

        return new AmazonS3Client(region);
    }

    private static RegionEndpoint ResolveRegion(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? RegionEndpoint.USEast1 : RegionEndpoint.GetBySystemName(value);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transcribe.Dispose();
        _s3.Dispose();

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
