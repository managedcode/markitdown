using System.Net;
using System.Net.Http;
using System.Text;
using MarkItDown;
using MarkItDown.Intelligence.Models;
using MarkItDown.Intelligence.Providers.Azure;
using Shouldly;

namespace MarkItDown.Tests.Intelligence;

public class AzureMediaTranscriptionProviderTests
{
    private sealed class StubArmTokenService : ArmTokenService
    {
        private readonly string? token;

        public StubArmTokenService(string? token)
            : base(token)
        {
            this.token = token;
        }

        public override Task<string?> GetArmTokenAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(token);
        }
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses = new();

        public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            responses.Enqueue(factory);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (responses.Count == 0)
            {
                throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
            }

            var response = responses.Dequeue()(request);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task TranscribeAsync_ReturnsSegmentsFromVideoIndexer()
    {
        var handler = new SequenceHandler();

        handler.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri!.Host.ShouldBe("management.azure.com");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"accessToken\": \"token123\", \"expirationTime\": \"2025-01-01T00:00:00Z\" }", Encoding.UTF8, "application/json")
            };
        });

        handler.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"id\": \"video123\" }", Encoding.UTF8, "application/json")
            };
        });

        handler.Enqueue(request =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"state\": \"Processed\" }", Encoding.UTF8, "application/json")
            };
        });

        handler.Enqueue(request =>
        {
            var transcriptJson = "{" +
                "\"state\":\"Processed\"," +
                "\"videos\":[{" +
                "\"id\":\"index-123\"," +
                "\"processingProgress\":\"100%\"," +
                "\"insights\":{" +
                "\"language\":\"en-US\"," +
                "\"duration\":\"0:00:10\"," +
                "\"speakers\":[{\"id\":1,\"name\":\"Speaker #1\"}]," +
                "\"sentiments\":[{\"sentimentType\":\"Neutral\",\"averageScore\":0.5,\"instances\":[{\"start\":\"0:00:00\",\"end\":\"0:00:10\"}]}]," +
                "\"topics\":[{\"name\":\"Health\",\"confidence\":0.998,\"instances\":[{\"start\":\"0:00:00\",\"end\":\"0:00:10\"}]}]," +
                "\"keywords\":[{\"text\":\"AGI\",\"confidence\":0.95,\"instances\":[{\"start\":\"0:00:00\",\"end\":\"0:00:10\"}]}]," +
                "\"transcript\":[" +
                "{\"text\":\"Hello world\",\"confidence\":0.72,\"speakerId\":1,\"instances\":[{\"start\":\"0:00:00\",\"end\":\"0:00:05\"}]}," +
                "{\"text\":\"Segment two\",\"confidence\":0.68,\"speakerId\":1,\"instances\":[{\"start\":\"0:00:05\",\"end\":\"0:00:10\"}]}" +
                "]}}]}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(transcriptJson, Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        };

        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "trial",
            SubscriptionId = "sub",
            ResourceGroup = "rg",
            AccountName = "account"
        };

        var provider = new AzureMediaTranscriptionProvider(options, httpClient, new StubArmTokenService("arm-token"));

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "audio/wav", extension: ".wav", fileName: "sample.wav");

        var result = await provider.TranscribeAsync(stream, streamInfo);

        result.ShouldNotBeNull();
        result!.Segments.Count.ShouldBe(2);
        result.Segments[0].Text.ShouldBe("Hello world");
        result.Segments[0].Start.ShouldBe(TimeSpan.Zero);
        result.Segments[0].End.ShouldBe(TimeSpan.FromSeconds(5));
        result.Segments[0].Metadata[MetadataKeys.Speaker].ShouldBe("Speaker #1");
        result.Segments[0].Metadata[MetadataKeys.Sentiment].ShouldBe("Neutral");
        result.Segments[0].Metadata[MetadataKeys.Topics].ShouldContain("Health");
        result.Segments[0].Metadata[MetadataKeys.Keywords].ShouldContain("AGI");
        result.Segments[1].Text.ShouldBe("Segment two");
        result.Metadata[MetadataKeys.Provider].ShouldBe(MetadataValues.ProviderAzureVideoIndexer);
        result.Metadata[MetadataKeys.Speakers].ShouldContain("Speaker #1");
        result.Metadata[MetadataKeys.Sentiments].ShouldContain("Neutral");
        result.Metadata[MetadataKeys.VideoIndexerState].ShouldBe("Processed");
        result.Metadata[MetadataKeys.VideoIndexerIndexId].ShouldBe("index-123");
        result.Metadata[MetadataKeys.VideoIndexerProgress].ShouldBe("100%");
        result.Segments[0].Metadata[MetadataKeys.VideoIndexerState].ShouldBe("Processed");
    }

    [Fact]
    public async Task TranscribeAsync_ReturnsNullWhenTokenUnavailable()
    {
        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "trial",
            AccountName = "account",
            SubscriptionId = "sub",
            ResourceGroup = "rg"
        };

        var provider = new AzureMediaTranscriptionProvider(options, new HttpClient(new SequenceHandler())
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        }, new StubArmTokenService(null));

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "audio/wav", extension: ".wav");

        var result = await provider.TranscribeAsync(stream, streamInfo);
        result.ShouldBeNull();
    }
}
