using System;
using System.IO;
using System.Net.Http;
using Azure;
using MarkItDown.Intelligence.Models;
using MarkItDown.Intelligence.Providers.Azure;
using Xunit;

namespace MarkItDown.Tests.Intelligence;

/// <summary>
/// Skeleton integration tests showing where to inject real Azure credentials. These stay skipped by default.
/// </summary>
public class AzureIntelligenceIntegrationTests
{
    private const string DocumentEndpointEnv = "AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT";
    private const string DocumentKeyEnv = "AZURE_DOCUMENT_INTELLIGENCE_KEY";
    private const string VisionEndpointEnv = "AZURE_VISION_ENDPOINT";
    private const string VisionKeyEnv = "AZURE_VISION_KEY";
    private const string VideoAccountIdEnv = "AZURE_VIDEO_INDEXER_ACCOUNT_ID";
    private const string VideoLocationEnv = "AZURE_VIDEO_INDEXER_LOCATION";
    private const string VideoSubscriptionEnv = "AZURE_VIDEO_INDEXER_SUBSCRIPTION_ID";
    private const string VideoResourceGroupEnv = "AZURE_VIDEO_INDEXER_RESOURCE_GROUP";
    private const string VideoArmTokenEnv = "AZURE_VIDEO_INDEXER_ARM_TOKEN";

    [Fact(Skip = "Set AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT and AZURE_DOCUMENT_INTELLIGENCE_KEY before enabling this smoke test.")]
    public async Task DocumentIntelligence_LiveSmokeTest()
    {
        var endpoint = Environment.GetEnvironmentVariable(DocumentEndpointEnv)!;
        var key = Environment.GetEnvironmentVariable(DocumentKeyEnv)!;

        var provider = new AzureDocumentIntelligenceProvider(new AzureDocumentIntelligenceOptions
        {
            Endpoint = endpoint,
            ApiKey = key,
            ModelId = "prebuilt-layout"
        });

        // Provide a real PDF path via environment variable when running locally.
        var samplePath = Environment.GetEnvironmentVariable("AZURE_DOCUMENT_INTELLIGENCE_SAMPLE_PDF")!;
        await using var stream = File.OpenRead(samplePath);
        var streamInfo = new StreamInfo(mimeType: "application/pdf", extension: ".pdf", fileName: Path.GetFileName(samplePath));

        DocumentIntelligenceResult? result = await provider.AnalyzeAsync(stream, streamInfo);
        Assert.NotNull(result);
        Assert.NotEmpty(result!.Pages);
    }

    [Fact(Skip = "Set AZURE_VISION_ENDPOINT and AZURE_VISION_KEY before enabling this smoke test.")]
    public async Task Vision_LiveSmokeTest()
    {
        var endpoint = Environment.GetEnvironmentVariable(VisionEndpointEnv)!;
        var key = Environment.GetEnvironmentVariable(VisionKeyEnv)!;

        var provider = new AzureImageUnderstandingProvider(new AzureVisionOptions
        {
            Endpoint = endpoint,
            ApiKey = key
        });

        var samplePath = Environment.GetEnvironmentVariable("AZURE_VISION_SAMPLE_IMAGE")!;
        await using var stream = File.OpenRead(samplePath);
        var streamInfo = new StreamInfo(mimeType: "image/png", extension: Path.GetExtension(samplePath), fileName: Path.GetFileName(samplePath));

        var result = await provider.AnalyzeAsync(stream, streamInfo);
        Assert.NotNull(result);
        Assert.True(!string.IsNullOrWhiteSpace(result!.Caption) || !string.IsNullOrWhiteSpace(result.Text));
    }

    [Fact(Skip = "Set Video Indexer env vars (see constants) before enabling this smoke test.")]
    public async Task VideoIndexer_LiveSmokeTest()
    {
        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = Environment.GetEnvironmentVariable(VideoAccountIdEnv)!,
            Location = Environment.GetEnvironmentVariable(VideoLocationEnv)!,
            SubscriptionId = Environment.GetEnvironmentVariable(VideoSubscriptionEnv),
            ResourceGroup = Environment.GetEnvironmentVariable(VideoResourceGroupEnv),
            ArmAccessToken = Environment.GetEnvironmentVariable(VideoArmTokenEnv)
        };

        using var httpClient = new HttpClient { BaseAddress = new Uri("https://api.videoindexer.ai/") };
        var provider = new AzureMediaTranscriptionProvider(options, httpClient);

        var samplePath = Environment.GetEnvironmentVariable("AZURE_VIDEO_INDEXER_SAMPLE_MEDIA")!;
        await using var stream = File.OpenRead(samplePath);
        var streamInfo = new StreamInfo(mimeType: "video/mp4", extension: ".mp4", fileName: Path.GetFileName(samplePath));

        var result = await provider.TranscribeAsync(stream, streamInfo);
        Assert.NotNull(result);
        Assert.NotEmpty(result!.Segments);
    }
}
