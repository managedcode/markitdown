using System;
using Amazon.Runtime;

namespace MarkItDown.Intelligence.Providers.Aws;

/// <summary>
/// Root configuration for AWS intelligence providers.
/// </summary>
public sealed class AwsIntelligenceOptions
{
    /// <summary>
    /// Configuration for AWS Textract. Set to <c>null</c> to disable.
    /// </summary>
    public AwsDocumentIntelligenceOptions? DocumentIntelligence { get; init; }

    /// <summary>
    /// Configuration for AWS Rekognition. Set to <c>null</c> to disable.
    /// </summary>
    public AwsVisionOptions? Vision { get; init; }

    /// <summary>
    /// Configuration for AWS Transcribe. Set to <c>null</c> to disable.
    /// </summary>
    public AwsMediaIntelligenceOptions? Media { get; init; }
}

public sealed class AwsDocumentIntelligenceOptions
{
    /// <summary>
    /// Explicit AWS credentials to use for Textract.
    /// </summary>
    public AWSCredentials? Credentials { get; init; }

    /// <summary>
    /// AWS access key id used when <see cref="Credentials"/> is not supplied.
    /// </summary>
    public string? AccessKeyId { get; init; }

    /// <summary>
    /// AWS secret access key used when <see cref="Credentials"/> is not supplied.
    /// </summary>
    public string? SecretAccessKey { get; init; }

    /// <summary>
    /// Optional session token used when <see cref="Credentials"/> is not supplied.
    /// </summary>
    public string? SessionToken { get; init; }

    /// <summary>
    /// AWS region identifier (e.g. <c>us-east-1</c>).
    /// </summary>
    public string? Region { get; init; }
}

public sealed class AwsVisionOptions
{
    public AWSCredentials? Credentials { get; init; }

    public string? AccessKeyId { get; init; }

    public string? SecretAccessKey { get; init; }

    public string? SessionToken { get; init; }

    public string? Region { get; init; }

    /// <summary>
    /// Minimum confidence threshold (0-100) for label detection.
    /// </summary>
    public float MinConfidence { get; init; } = 75f;

    /// <summary>
    /// Maximum number of labels to return.
    /// </summary>
    public int MaxLabels { get; init; } = 10;
}

public sealed class AwsMediaIntelligenceOptions
{
    public AWSCredentials? Credentials { get; init; }

    public string? AccessKeyId { get; init; }

    public string? SecretAccessKey { get; init; }

    public string? SessionToken { get; init; }

    public string? Region { get; init; }

    /// <summary>
    /// S3 bucket used for uploading audio/video payloads.
    /// </summary>
    public string? InputBucketName { get; init; }

    /// <summary>
    /// Optional prefix inside the input bucket for uploads.
    /// </summary>
    public string? InputKeyPrefix { get; init; }

    /// <summary>
    /// When supplied the transcript json is written to this bucket; otherwise AWS Transcribe chooses the default output.
    /// </summary>
    public string? OutputBucketName { get; init; }

    /// <summary>
    /// Optional prefix for transcript outputs.
    /// </summary>
    public string? OutputKeyPrefix { get; init; }

    /// <summary>
    /// Preferred language code (e.g. <c>en-US</c>).
    /// </summary>
    public string LanguageCode { get; init; } = "en-US";

    /// <summary>
    /// When true uploaded media objects are removed after transcription completes.
    /// </summary>
    public bool DeleteInputOnCompletion { get; init; } = true;

    /// <summary>
    /// Polling interval while waiting for transcription results.
    /// </summary>
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(5);
}
