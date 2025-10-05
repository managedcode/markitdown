using Google.Apis.Auth.OAuth2;

namespace MarkItDown.Intelligence.Providers.Google;

/// <summary>
/// Root configuration for Google Cloud intelligence providers.
/// </summary>
public sealed class GoogleIntelligenceOptions
{
    /// <summary>
    /// Configuration for Google Document AI. Set to <c>null</c> to disable.
    /// </summary>
    public GoogleDocumentIntelligenceOptions? DocumentIntelligence { get; init; }

    /// <summary>
    /// Configuration for Google Vision. Set to <c>null</c> to disable.
    /// </summary>
    public GoogleVisionOptions? Vision { get; init; }

    /// <summary>
    /// Configuration for Google Speech-to-Text. Set to <c>null</c> to disable.
    /// </summary>
    public GoogleMediaIntelligenceOptions? Media { get; init; }
}

public sealed class GoogleDocumentIntelligenceOptions
{
    /// <summary>
    /// Google Cloud project identifier.
    /// </summary>
    public string? ProjectId { get; init; }

    /// <summary>
    /// Region hosting the Document AI processor, e.g. <c>us</c> or <c>eu</c>.
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Processor identifier (processor name without the resource prefix).
    /// </summary>
    public string? ProcessorId { get; init; }

    /// <summary>
    /// Optional path to a service account json file.
    /// </summary>
    public string? CredentialsPath { get; init; }

    /// <summary>
    /// Raw json credentials for the service account. Takes precedence over <see cref="CredentialsPath"/>.
    /// </summary>
    public string? JsonCredentials { get; init; }

    /// <summary>
    /// Explicit Google credential to use instead of file or json configuration.
    /// </summary>
    public GoogleCredential? Credential { get; init; }
}

public sealed class GoogleVisionOptions
{
    /// <summary>
    /// Optional path to a service account json file.
    /// </summary>
    public string? CredentialsPath { get; init; }

    /// <summary>
    /// Raw json credentials for the service account. Takes precedence over <see cref="CredentialsPath"/>.
    /// </summary>
    public string? JsonCredentials { get; init; }

    /// <summary>
    /// Explicit Google credential to use instead of file or json configuration.
    /// </summary>
    public GoogleCredential? Credential { get; init; }

    /// <summary>
    /// Maximum number of labels to return from Vision.
    /// </summary>
    public int MaxLabels { get; init; } = 10;

    /// <summary>
    /// Minimum confidence threshold (0-100) for Vision labels.
    /// </summary>
    public float MinConfidence { get; init; } = 70f;
}

public sealed class GoogleMediaIntelligenceOptions
{
    /// <summary>
    /// Optional path to a service account json file.
    /// </summary>
    public string? CredentialsPath { get; init; }

    /// <summary>
    /// Raw json credentials for the service account. Takes precedence over <see cref="CredentialsPath"/>.
    /// </summary>
    public string? JsonCredentials { get; init; }

    /// <summary>
    /// Explicit Google credential to use instead of file or json configuration.
    /// </summary>
    public GoogleCredential? Credential { get; init; }

    /// <summary>
    /// Preferred language code for transcripts, e.g. <c>en-US</c>.
    /// </summary>
    public string LanguageCode { get; init; } = "en-US";

    /// <summary>
    /// Optional sample rate for the audio stream.
    /// </summary>
    public int? SampleRateHertz { get; init; }

    /// <summary>
    /// When true enables automatic punctuation in transcripts.
    /// </summary>
    public bool EnableAutomaticPunctuation { get; init; } = true;
}
