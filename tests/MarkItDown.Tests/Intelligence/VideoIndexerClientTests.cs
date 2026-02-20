using System.Net;
using System.Text;
using System.Text.Json;
using MarkItDown;
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
    public async Task UploadAsync_ResourceIdWithoutLeadingSlash_IsNormalizedForArmRequest()
    {
        var sequence = new SequenceHandler();

        sequence.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri!.ToString().ShouldContain("https://management.azure.com/subscriptions/sub/resourceGroups/rg/providers/Microsoft.VideoIndexer/accounts/account/generateAccessToken");
            request.RequestUri!.ToString().ShouldNotContain("https://management.azure.comsubscriptions/");

            var payload = new
            {
                accessToken = "token123",
                expirationTime = "2025-01-01T00:00:00Z"
            };

            return JsonResponse(HttpStatusCode.OK, payload);
        });

        sequence.Enqueue(_ =>
            JsonResponse(HttpStatusCode.OK, new { id = "video123" }));

        using var httpClient = new HttpClient(sequence)
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        };

        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "trial",
            ResourceId = "subscriptions/sub/resourceGroups/rg/providers/Microsoft.VideoIndexer/accounts/account/"
        };

        var client = new VideoIndexerClient(options, httpClient, new StubArmTokenService("arm-token"));
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "video/mp4", extension: ".mp4", fileName: "sample.mp4");

        var result = await client.UploadAsync(stream, streamInfo, CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Value.VideoId.ShouldBe("video123");
    }

    [Theory]
    [InlineData("subscriptions/sub/resourceGroups/rg/providers/Microsoft.VideoIndexer/accounts/account/overview")]
    [InlineData("/subscriptions/sub/resourceGroups/rg/providers/Microsoft.VideoIndexer/accounts/account/overview/")]
    [InlineData("https://management.azure.com/subscriptions/sub/resourceGroups/rg/providers/Microsoft.VideoIndexer/accounts/account/overview/?api-version=2024-01-01")]
    public async Task UploadAsync_ResourceIdWithExtraSegments_IsNormalizedToAccountPath(string resourceId)
    {
        var sequence = new SequenceHandler();

        sequence.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri!.ToString().ShouldContain("https://management.azure.com/subscriptions/sub/resourceGroups/rg/providers/Microsoft.VideoIndexer/accounts/account/generateAccessToken");
            request.RequestUri!.ToString().ShouldNotContain("/overview");
            request.RequestUri!.ToString().ShouldNotContain("https://management.azure.comsubscriptions/");

            var payload = new
            {
                accessToken = "token123",
                expirationTime = "2025-01-01T00:00:00Z"
            };

            return JsonResponse(HttpStatusCode.OK, payload);
        });

        sequence.Enqueue(_ =>
            JsonResponse(HttpStatusCode.OK, new { id = "video123" }));

        using var httpClient = new HttpClient(sequence)
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        };

        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "trial",
            ResourceId = resourceId
        };

        var client = new VideoIndexerClient(options, httpClient, new StubArmTokenService("arm-token"));
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "video/mp4", extension: ".mp4", fileName: "sample.mp4");

        var result = await client.UploadAsync(stream, streamInfo, CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Value.VideoId.ShouldBe("video123");
    }

    [Fact]
    public async Task UploadAsync_WithVideoIndexerAccountAccessToken_SkipsArmGenerateAccessToken()
    {
        var sequence = new SequenceHandler();

        sequence.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri!.ToString().ShouldContain("/trial/Accounts/account/Videos?");
            request.RequestUri!.Query.ShouldContain("accessToken=");
            request.RequestUri!.ToString().ShouldNotContain("management.azure.com");
            return JsonResponse(HttpStatusCode.OK, new { id = "video123" });
        });

        using var httpClient = new HttpClient(sequence)
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        };

        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "trial",
            ResourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.VideoIndexer/accounts/account"
        };

        var token = BuildUnsignedJwtWithAudience("https://api.videoindexer.ai/", DateTimeOffset.UtcNow.AddMinutes(30));
        var client = new VideoIndexerClient(options, httpClient, new StubArmTokenService(token));
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "video/mp4", extension: ".mp4", fileName: "sample.mp4");

        var result = await client.UploadAsync(stream, streamInfo, CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Value.VideoId.ShouldBe("video123");
        result.Value.AccountAccessToken.ShouldBe(token);
    }

    [Fact]
    public async Task UploadAsync_WithReadOnlyVideoIndexerAccountToken_FailsFastWithActionableError()
    {
        using var httpClient = new HttpClient(new SequenceHandler())
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        };

        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "trial",
            ResourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.VideoIndexer/accounts/account"
        };

        var readOnlyToken = BuildUnsignedJwtWithAudience("https://api.videoindexer.ai/", DateTimeOffset.UtcNow.AddMinutes(30), permission: "Reader");
        var client = new VideoIndexerClient(options, httpClient, new StubArmTokenService(readOnlyToken));
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "video/mp4", extension: ".mp4", fileName: "sample.mp4");

        var exception = await Should.ThrowAsync<FileConversionException>(() => client.UploadAsync(stream, streamInfo, CancellationToken.None));

        exception.Message.ShouldContain("Permission=Reader");
        exception.Message.ShouldContain("Contributor");
    }

    [Fact]
    public async Task UploadAsync_WhenNameConflicts_RetriesWithGeneratedName()
    {
        var sequence = new SequenceHandler();

        sequence.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri!.ToString().ShouldContain("/trial/Accounts/account/Videos?");
            request.RequestUri!.Query.ShouldContain("name=sample.mp4");
            return new HttpResponseMessage(HttpStatusCode.Conflict);
        });

        sequence.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri!.ToString().ShouldContain("/trial/Accounts/account/Videos?");
            request.RequestUri!.Query.ShouldContain("name=sample-");
            request.RequestUri!.Query.ShouldContain(".mp4");
            return JsonResponse(HttpStatusCode.OK, new { id = "video123" });
        });

        using var httpClient = new HttpClient(sequence)
        {
            BaseAddress = new Uri("https://api.videoindexer.ai/")
        };

        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "trial",
            ResourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.VideoIndexer/accounts/account"
        };

        var token = BuildUnsignedJwtWithAudience("https://api.videoindexer.ai/", DateTimeOffset.UtcNow.AddMinutes(30));
        var client = new VideoIndexerClient(options, httpClient, new StubArmTokenService(token));
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamInfo = new StreamInfo(mimeType: "video/mp4", extension: ".mp4", fileName: "sample.mp4");

        var result = await client.UploadAsync(stream, streamInfo, CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Value.VideoId.ShouldBe("video123");
        result.Value.FileName.ShouldStartWith("sample-");
        result.Value.FileName.ShouldEndWith(".mp4");
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
    public async Task WaitForProcessingAsync_WhenFailed_ThrowsFileConversionException()
    {
        var sequence = new SequenceHandler();
        sequence.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Get);
            request.RequestUri!.AbsolutePath.ShouldContain("/Videos/video123/Index");
            request.RequestUri.Query.ShouldContain("accessToken=token123");
            return JsonResponse(HttpStatusCode.OK, new { state = "Failed" });
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

        var exception = await Should.ThrowAsync<FileConversionException>(
            () => client.WaitForProcessingAsync("video123", "token123", CancellationToken.None));

        exception.Message.ShouldContain("failed");
        exception.Message.ShouldContain("video123");
    }

    [Fact]
    public async Task WaitForProcessingAsync_WhenProcessingNeverCompletes_ThrowsTimeout()
    {
        var sequence = new SequenceHandler();
        sequence.Enqueue(request =>
        {
            request.Method.ShouldBe(HttpMethod.Get);
            request.RequestUri!.AbsolutePath.ShouldContain("/Videos/video123/Index");
            request.RequestUri.Query.ShouldContain("accessToken=token123");
            return JsonResponse(HttpStatusCode.OK, new { state = "Processing", processingProgress = "42%" });
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
            ResourceGroup = "rg",
            PollingInterval = TimeSpan.FromMilliseconds(1),
            MaxProcessingTime = TimeSpan.FromTicks(1)
        };

        var client = new VideoIndexerClient(options, httpClient, new StubArmTokenService("arm-token"));

        var exception = await Should.ThrowAsync<FileConversionException>(
            () => client.WaitForProcessingAsync("video123", "token123", CancellationToken.None));

        exception.Message.ShouldContain("timed out");
        exception.Message.ShouldContain("Processing");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WhenPollingIntervalIsNotPositive_ThrowsArgumentException(int milliseconds)
    {
        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "trial",
            ResourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.VideoIndexer/accounts/account",
            PollingInterval = TimeSpan.FromMilliseconds(milliseconds)
        };

        var exception = Should.Throw<ArgumentException>(() =>
            _ = new VideoIndexerClient(options, new HttpClient(new SequenceHandler())
            {
                BaseAddress = new Uri("https://api.videoindexer.ai/")
            }, new StubArmTokenService("token")));

        exception.Message.ShouldContain("polling interval");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WhenMaxProcessingTimeIsNotPositive_ThrowsArgumentException(int milliseconds)
    {
        var options = new AzureMediaIntelligenceOptions
        {
            AccountId = "account",
            Location = "trial",
            ResourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.VideoIndexer/accounts/account",
            MaxProcessingTime = TimeSpan.FromMilliseconds(milliseconds)
        };

        var exception = Should.Throw<ArgumentException>(() =>
            _ = new VideoIndexerClient(options, new HttpClient(new SequenceHandler())
            {
                BaseAddress = new Uri("https://api.videoindexer.ai/")
            }, new StubArmTokenService("token")));

        exception.Message.ShouldContain("max processing time");
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

    private static string BuildUnsignedJwtWithAudience(string audience, DateTimeOffset expiresOn, string? permission = null)
    {
        static string Encode(object value)
        {
            var json = JsonSerializer.Serialize(value);
            var bytes = Encoding.UTF8.GetBytes(json);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        var header = Encode(new { alg = "none", typ = "JWT" });
        var payloadBody = new Dictionary<string, object?>
        {
            ["aud"] = audience,
            ["exp"] = expiresOn.ToUnixTimeSeconds()
        };

        if (!string.IsNullOrWhiteSpace(permission))
        {
            payloadBody["Permission"] = permission;
        }

        var payload = Encode(payloadBody);

        return $"{header}.{payload}.";
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
