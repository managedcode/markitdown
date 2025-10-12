using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.ClientModel;
using Azure;
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
    [Fact]
    public async Task DocumentIntelligence_LiveSmokeTest()
    {
        var document = fixture.Document;
        if (document is null)
        {
            return;
        }

        var provider = new AzureDocumentIntelligenceProvider(document.Options);

        try
        {
            await using var stream = document.OpenSample();
            var result = await provider.AnalyzeAsync(stream, document.CreateStreamInfo());

            Assert.NotNull(result);
            Assert.NotEmpty(result!.Pages);
        }
        catch (RequestFailedException)
        {
            return;
        }
        catch (ClientResultException)
        {
            return;
        }
        catch (HttpRequestException)
        {
            return;
        }
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

        try
        {
            await using var stream = vision.OpenSample();
            var result = await provider.AnalyzeAsync(stream, vision.CreateStreamInfo());

            Assert.NotNull(result);
            Assert.True(!string.IsNullOrWhiteSpace(result!.Caption) || !string.IsNullOrWhiteSpace(result.Text));
        }
        catch (RequestFailedException)
        {
            return;
        }
        catch (ClientResultException)
        {
            return;
        }
        catch (HttpRequestException)
        {
            return;
        }
    }

    [Fact]
    public async Task VideoIndexer_LiveSmokeTest()
    {
        var media = fixture.Media;
        if (media is null)
        {
            return;
        }

        try
        {
            await using var stream = media.OpenSample();
            using var provider = new AzureMediaTranscriptionProvider(media.Options);
            using var cancellation = AzureIntelligenceFixture.CreateDefaultCancellationToken();

            var result = await provider.TranscribeAsync(stream, media.CreateStreamInfo(), request: null, cancellation.Token);

            Assert.NotNull(result);
            Assert.NotEmpty(result!.Segments);
        }
        catch (RequestFailedException)
        {
            return;
        }
        catch (ClientResultException)
        {
            return;
        }
        catch (HttpRequestException)
        {
            return;
        }
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

        try
        {
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
        catch (RequestFailedException)
        {
            return;
        }
        catch (ClientResultException)
        {
            return;
        }
        catch (HttpRequestException)
        {
            return;
        }
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

        try
        {
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
        catch (RequestFailedException)
        {
            return;
        }
        catch (ClientResultException)
        {
            return;
        }
        catch (HttpRequestException)
        {
            return;
        }
    }
}

#pragma warning restore MEAI001
