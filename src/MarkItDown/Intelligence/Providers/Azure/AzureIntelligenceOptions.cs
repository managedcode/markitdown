namespace MarkItDown.Intelligence.Providers.Azure;

/// <summary>
/// Root options for enabling Azure-based intelligence providers.
/// </summary>
public sealed class AzureIntelligenceOptions
{
    /// <summary>
    /// Document Intelligence configuration. Set to <c>null</c> to disable.
    /// </summary>
    public AzureDocumentIntelligenceOptions? DocumentIntelligence { get; init; }

    /// <summary>
    /// Computer Vision / Image Analysis configuration. Set to <c>null</c> to disable.
    /// </summary>
    public AzureVisionOptions? Vision { get; init; }

    /// <summary>
    /// Video Indexer / Media transcription configuration. Set to <c>null</c> to disable.
    /// </summary>
    public AzureMediaIntelligenceOptions? Media { get; init; }
}

public sealed class AzureDocumentIntelligenceOptions
{
    public string? Endpoint { get; init; }

    public string? ApiKey { get; init; }

    /// <summary>
    /// Model identifier to use (e.g. <c>prebuilt-layout</c>).
    /// </summary>
    public string ModelId { get; init; } = "prebuilt-layout";
}

public sealed class AzureVisionOptions
{
    public string? Endpoint { get; init; }

    public string? ApiKey { get; init; }
}

public sealed class AzureMediaIntelligenceOptions
{
    /// <summary>
    /// Azure Video Indexer account identifier.
    /// </summary>
    public string? AccountId { get; init; }

    /// <summary>
    /// Azure Video Indexer account name (used when composing resource IDs).
    /// </summary>
    public string? AccountName { get; init; }

    /// <summary>
    /// Azure Video Indexer account location (e.g. <c>trial</c>, <c>eastus2</c>).
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Azure subscription id hosting the Video Indexer account.
    /// </summary>
    public string? SubscriptionId { get; init; }

    /// <summary>
    /// Azure resource group of the Video Indexer account.
    /// </summary>
    public string? ResourceGroup { get; init; }

    /// <summary>
    /// Full ARM resource id for the Video Indexer account (overrides subscription/resource group/account name when provided).
    /// </summary>
    public string? ResourceId { get; init; }

    /// <summary>
    /// Optional static ARM token. If not provided the SDK will attempt to use <see cref="Azure.Identity.DefaultAzureCredential"/>.
    /// </summary>
    public string? ArmAccessToken { get; init; }
}
