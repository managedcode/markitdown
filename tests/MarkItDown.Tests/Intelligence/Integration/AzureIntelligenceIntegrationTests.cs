using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown.Intelligence;
using MarkItDown.Intelligence.Models;
using MarkItDown.Intelligence.Providers.Azure;
using Microsoft.Extensions.AI;
using OpenAI.Audio;
using Xunit;

using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatOptions = Microsoft.Extensions.AI.ChatOptions;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

#pragma warning disable MEAI001
namespace MarkItDown.Tests.Intelligence.Integration;

/// <summary>
/// Live integration tests for Azure intelligence providers. These are only executed when credentials are supplied.
/// </summary>
public class AzureIntelligenceIntegrationTests(AzureIntelligenceFixture fixture) : IClassFixture<AzureIntelligenceFixture>
{
    private const string HardcodedVideoIndexerArmAccessToken = "TOKEN";
    private const string HardcodedVideoIndexerAccountId = "ACCOUNT_GUID";
    private const string HardcodedVideoIndexerResourceId = "/subscriptions/SUBSCRIPTION-GUID/resourcegroups/AzureAI/providers/Microsoft.VideoIndexer/accounts/ACCOUNT_NAME/";

    private static readonly AzureMediaIntelligenceOptions HardcodedVideoIndexerOptions = new()
    {
        AccountId = HardcodedVideoIndexerAccountId,
        Location = "eastus",
        ResourceId = HardcodedVideoIndexerResourceId,
        ArmAccessToken = HardcodedVideoIndexerArmAccessToken
    };

    [Fact]
    public async Task DocumentIntelligence_LiveSmokeTest()
    {
        var document = fixture.Document;
        if (document is null)
        {
            return;
        }

        var provider = new AzureDocumentIntelligenceProvider(document.Options);

        await using var stream = document.OpenSample();
        var result = await provider.AnalyzeAsync(stream, document.CreateStreamInfo());

        Assert.NotNull(result);
        Assert.NotEmpty(result!.Pages);
    }

    [Fact]
    public async Task Vision_LiveSmokeTest()
    {
        var vision = fixture.Vision;
        if (vision is null)
        {
            return;
        }

        var provider = new AzureImageUnderstandingProvider(vision.Options);

        await using var stream = vision.OpenSample();
        var result = await provider.AnalyzeAsync(stream, vision.CreateStreamInfo());

        Assert.NotNull(result);
        Assert.True(!string.IsNullOrWhiteSpace(result!.Caption) || !string.IsNullOrWhiteSpace(result.Text));
    }

    [Fact]
    public async Task VideoIndexer_LiveSmokeTest()
    {
        var media = fixture.Media;
        if (media is null)
        {
            return;
        }

        await using var stream = media.OpenSample();
        using var provider = new AzureMediaTranscriptionProvider(media.Options);
        using var cancellation = AzureIntelligenceFixture.CreateDefaultCancellationToken();

        var result = await provider.TranscribeAsync(stream, media.CreateStreamInfo(), request: null, cancellation.Token);

        Assert.NotNull(result);
        Assert.NotEmpty(result!.Segments);
    }

    [Fact]
    public async Task VideoIndexer_MarkItDownClient_LiveMp4ToMarkdown()
    {
        if (!IsHardcodedVideoIndexerConfigured())
        {
            return;
        }

        var videoPath = TestAssetLoader.GetAssetPath(TestAssetCatalog.VideoIndexerTestVideoMp4);
        var options = new MarkItDownOptions
        {
            AzureIntelligence = new AzureIntelligenceOptions
            {
                Media = HardcodedVideoIndexerOptions
            }
        };

        var request = ConversionRequest.FromConfiguration(builder =>
        {
            builder.UseMediaTranscription(new MediaTranscriptionRequest(
                PreferredProvider: MediaTranscriptionProviderKind.Azure,
                Language: "English"));
        });

        var client = new global::MarkItDown.MarkItDownClient(options);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(15));

        var result = await client.ConvertAsync(videoPath, request, cancellation.Token);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Markdown));
        Assert.Contains("### Video Transcript", result.Markdown);
        Assert.Contains("### Video Analysis", result.Markdown);
        Assert.Contains("#### Overview", result.Markdown);
        Assert.Contains("[", result.Markdown); // timing header prefix: [mm:ss-mm:ss]
        Assert.Contains("Emotion/Sentiment", result.Markdown);
        Assert.Contains("Topics", result.Markdown);
        Assert.Contains("Keywords", result.Markdown);
        Assert.Contains("Video Indexer State", result.Markdown);
        Assert.NotEmpty(result.Segments);
        var hasAzureVideoIndexerTranscript = result.Segments.Any(segment =>
            segment.Type == SegmentType.Audio &&
            segment.AdditionalMetadata.TryGetValue(MetadataKeys.Provider, out var provider) &&
            provider == MetadataValues.ProviderAzureVideoIndexer);
        Assert.True(
            hasAzureVideoIndexerTranscript,
            "Expected Azure Video Indexer transcript segments in Markdown conversion output. " +
            "If this fails, verify HardcodedVideoIndexerOptions credentials and Video Indexer account access.");
        var audioSegments = result.Segments.Where(segment => segment.Type == SegmentType.Audio).ToList();
        Assert.NotEmpty(audioSegments);
        Assert.Contains(audioSegments, segment => segment.StartTime.HasValue && segment.EndTime.HasValue && segment.EndTime > segment.StartTime);
        Assert.Contains(audioSegments, segment => segment.AdditionalMetadata.ContainsKey(MetadataKeys.Sentiment));
        Assert.Contains(audioSegments, segment => segment.AdditionalMetadata.ContainsKey(MetadataKeys.Topics));
        Assert.Contains(audioSegments, segment => segment.AdditionalMetadata.ContainsKey(MetadataKeys.Keywords));
        Assert.Contains(audioSegments, segment => segment.AdditionalMetadata.ContainsKey(MetadataKeys.VideoIndexerState));
        Assert.Contains(audioSegments, segment => segment.AdditionalMetadata.ContainsKey(MetadataKeys.VideoIndexerIndexId));
        Assert.Contains(audioSegments, segment => segment.AdditionalMetadata.ContainsKey(MetadataKeys.VideoIndexerProgress));

        var videoId = result.Segments
            .Where(segment =>
                segment.Type == SegmentType.Audio &&
                segment.AdditionalMetadata.TryGetValue(MetadataKeys.Provider, out var provider) &&
                provider == MetadataValues.ProviderAzureVideoIndexer &&
                segment.AdditionalMetadata.TryGetValue(MetadataKeys.VideoId, out var id) &&
                !string.IsNullOrWhiteSpace(id))
            .Select(segment => segment.AdditionalMetadata[MetadataKeys.VideoId])
            .FirstOrDefault();

        Assert.False(string.IsNullOrWhiteSpace(videoId), "Expected Azure Video Indexer transcript segments to include video id metadata.");
        await AssertProcessedInVideoIndexerAsync(videoId!, HardcodedVideoIndexerOptions, cancellation.Token);
    }

    [Fact]
    public async Task LanguageModels_ImagePrompt_LiveSmokeTest()
    {
        var language = fixture.LanguageModels;
        if (language is null)
        {
            return;
        }

        var aiModels = fixture.AiModels;
        if (aiModels?.ChatClient is not IChatClient chatClient)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(language.ImageSamplePath))
        {
            return;
        }

        var imageBytes = await File.ReadAllBytesAsync(language.ImageSamplePath);
        using var cancellation = AzureIntelligenceFixture.CreateDefaultCancellationToken();

        var messages = new[]
        {
            new AiChatMessage(AiChatRole.System, "You are an assistant that provides concise descriptions of images."),
            new AiChatMessage(
                AiChatRole.User,
                new List<AIContent>
                {
                    new TextContent("Describe this image in one sentence."),
                    new DataContent(imageBytes, language.ImageSampleMimeType ?? "image/png")
                })
        };

        var response = await chatClient.GetResponseAsync(messages, new AiChatOptions
        {
            Temperature = 0,
            MaxOutputTokens = 256
        }, cancellation.Token);

        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response.Text));
    }

    [Fact]
    public async Task LanguageModels_SpeechToText_LiveSmokeTest()
    {
        var language = fixture.LanguageModels;
        if (language is null)
        {
            return;
        }

        var aiModels = fixture.AiModels;
        if (aiModels?.SpeechToTextClient is not ISpeechToTextClient speechClient)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(language.AudioSamplePath))
        {
            return;
        }

        await using var stream = File.OpenRead(language.AudioSamplePath);
        using var cancellation = AzureIntelligenceFixture.CreateDefaultCancellationToken();

        var response = await speechClient.GetTextAsync(stream, new SpeechToTextOptions
        {
            ModelId = language.SpeechModelId,
            SpeechLanguage = language.SpeechLanguage
        }, cancellation.Token);

        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response.Text));
    }

    private static async Task AssertProcessedInVideoIndexerAsync(string videoId, AzureMediaIntelligenceOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ArmAccessToken))
        {
            throw new InvalidOperationException("Hardcoded Video Indexer test token is not configured.");
        }

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        };

        var requestUri = $"{options.Location}/Accounts/{options.AccountId}/Videos/{videoId}/Index?accessToken={Uri.EscapeDataString(options.ArmAccessToken)}";
        using var response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("state", out var stateNode), $"Video Indexer response for '{videoId}' has no state.");
        var state = stateNode.GetString();
        Assert.Equal("Processed", state);

        Assert.True(root.TryGetProperty("videos", out var videos) && videos.GetArrayLength() > 0, $"Video Indexer response for '{videoId}' has no videos array.");
        var insights = videos[0].GetProperty("insights");
        Assert.True(insights.TryGetProperty("transcript", out var transcript) && transcript.GetArrayLength() > 0, $"Video Indexer response for '{videoId}' has no transcript entries.");
    }

    private static bool IsHardcodedVideoIndexerConfigured()
    {
        if (IsPlaceholderValue(HardcodedVideoIndexerArmAccessToken) ||
            IsPlaceholderValue(HardcodedVideoIndexerAccountId) ||
            IsPlaceholderValue(HardcodedVideoIndexerResourceId))
        {
            return false;
        }

        return true;
    }

    private static bool IsPlaceholderValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = value.Trim();
        return normalized.Equals("TOKEN", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("ACCOUNT_GUID", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("SUBSCRIPTION-GUID", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("ACCOUNT_NAME", StringComparison.OrdinalIgnoreCase);
    }
}

#pragma warning restore MEAI001
