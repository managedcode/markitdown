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
                "\"videos\":[{" +
                "\"insights\":{\"transcript\":[" +
                "{\"text\":\"Hello world\",\"start\":\"0:00:00\",\"duration\":\"0:00:05\"}," +
                "{\"text\":\"Segment two\",\"start\":\"0:00:05\",\"duration\":\"0:00:05\"}" +
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
        result.Segments[1].Text.ShouldBe("Segment two");
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
