using System.Net;
using System.Text;
using System.Text.Json;
using MarkItDown.Intelligence.Providers.Azure;
using MarkItDown.Intelligence.Providers.Azure.VideoIndexer;
using Shouldly;
using Xunit;

namespace MarkItDown.Tests.Intelligence;

public class VideoIndexerClientTests
{
    [Fact]
    public async Task UploadAsync_UsesComputedResourceIdAndReturnsVideoId()
    {
        var sequence = new SequenceHandler();

        sequence.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri!.ToString().ShouldContain("/subscriptions/sub/resourceGroups/rg/providers/Microsoft.VideoIndexer/accounts/account/generateAccessToken");
            request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
            var payload = new
            {
                accessToken = "token123",
                expirationTime = "2025-01-01T00:00:00Z"
            };
            return JsonResponse(HttpStatusCode.OK, payload);
        });

        sequence.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri!.ToString().ShouldContain("/trial/Accounts/account/Videos?");
            request.RequestUri!.Query.ShouldContain("accessToken=token123");
            var payload = new { id = "video123" };
            return JsonResponse(HttpStatusCode.OK, payload);
        });

        using var httpClient = new HttpClient(sequence)
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        };

        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            AccountName = "account",
            Location = "trial",
            SubscriptionId = "sub",
            ResourceGroup = "rg"
        };

        var client = new VideoIndexerClient(options, httpClient, new StubArmTokenService("arm-token"));
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "video/mp4", extension: ".mp4", fileName: "sample.mp4");

        var result = await client.UploadAsync(stream, streamInfo, CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Value.VideoId.ShouldBe("video123");
        result.Value.AccountAccessToken.ShouldBe("token123");
    }

    [Fact]
    public async Task WaitForProcessingAsync_StopsWhenProcessed()
    {
        var sequence = new SequenceHandler();

        sequence.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Get);
            request.RequestUri!.AbsolutePath.ShouldContain("/Videos/video123/Index");
            request.RequestUri.Query.ShouldContain("accessToken=token123");
            var payload = new { state = "Processed" };
            return JsonResponse(HttpStatusCode.OK, payload);
        });

        using var httpClient = new HttpClient(sequence)
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        };

        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            AccountName = "account",
            Location = "trial",
            SubscriptionId = "sub",
            ResourceGroup = "rg"
        };

        var client = new VideoIndexerClient(options, httpClient, new StubArmTokenService("arm-token"));
        await client.WaitForProcessingAsync("video123", "token123", CancellationToken.None);
    }

    [Fact]
    public async Task GetVideoIndexAsync_ReturnsDocumentWhenAvailable()
    {
        var sequence = new SequenceHandler();
        sequence.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Get);
            request.RequestUri!.AbsolutePath.ShouldContain("/Videos/video123/Index");
            request.RequestUri.Query.ShouldContain("accessToken=token123");
            request.RequestUri.Query.ShouldContain("language=English");
            var payload = new
            {
                videos = new[]
                {
                    new
                    {
                        insights = new
                        {
                            transcript = new[]
                            {
                                new { text = "hello", start = "0:00:00", duration = "0:00:05" }
                            }
                        }
                    }
                }
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(sequence)
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        };

        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            AccountName = "account",
            Location = "trial",
            SubscriptionId = "sub",
            ResourceGroup = "rg"
        };

        var client = new VideoIndexerClient(options, httpClient, new StubArmTokenService("arm-token"));
        using var document = await client.GetVideoIndexAsync("video123", "token123", language: null, CancellationToken.None);
        document.ShouldNotBeNull();
        document!.RootElement.GetProperty("videos").GetArrayLength().ShouldBe(1);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return new HttpResponseMessage(statusCode) { Content = content };
    }

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
}
