using ManagedCode.Storage.Core;
using MarkItDown;

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

    /// <summary>
    /// Optional storage factory used when transcription requests choose <see cref="MediaUploadRoute.StorageUrl"/>.
    /// The created <see cref="IStorage"/> uploads local media and must return metadata with an HTTP/S URI reachable by Azure Video Indexer.
    /// </summary>
    public Func<IStorage>? UploadStorageFactory { get; init; }

    /// <summary>
    /// Optional resolver for upload directory names when using <see cref="UploadStorageFactory"/>.
    /// </summary>
    public Func<StreamInfo, string>? UploadStorageDirectoryResolver { get; init; }

    /// <summary>
    /// Polling cadence used while waiting for Azure Video Indexer processing.
    /// </summary>
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum time to wait for Azure Video Indexer to report a processed state.
    /// </summary>
    public TimeSpan MaxProcessingTime { get; init; } = TimeSpan.FromMinutes(15);
}
