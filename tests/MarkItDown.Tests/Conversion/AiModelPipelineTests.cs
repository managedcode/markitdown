#pragma warning disable MEAI001
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using MarkItDown;
using MarkItDown.Intelligence;
using MarkItDown.Conversion.Middleware;

using MarkItDown.Tests.Fixtures;
using Microsoft.Extensions.AI;
using Shouldly;

namespace MarkItDown.Tests.Conversion;

public class AiModelPipelineTests
{
    [Fact]
    public async Task ImageEnrichment_PreservesLiteralRegexReplacementTokens()
    {
        const string description = "Shell syntax: $& $$ $1 $` $' $_ ${name}.";
        var chatClient = new RecordingChatClient(description);
        var client = new MarkItDownClient(new MarkItDownOptions
        {
            AiModels = new StaticAiModelProvider(chatClient, null),
            EnableAiImageEnrichment = true
        });
        await using var stream = DocxInlineImageFactory.Create();

        var result = await client.ConvertAsync(stream, new StreamInfo(extension: ".docx", fileName: "inline-images.docx"));

        result.Artifacts.Images.ShouldNotBeEmpty();
        result.Artifacts.Images[0].DetailedDescription.ShouldBe(description);
        result.Markdown.ShouldContain($"<!-- Image description:{Environment.NewLine}{description}{Environment.NewLine}");
    }

    [Fact]
    public async Task ImageEnrichment_ExistingImageSegmentPreservesLiteralRegexReplacementTokens()
    {
        const string description = "Shell syntax: $& $$ $1 $` $' $_ ${name}.";
        const string placeholder = "**Image**";
        var image = new ImageArtifact([1, 2, 3], ManagedCode.MimeTypes.MimeHelper.PNG)
        {
            PlaceholderMarkdown = placeholder,
            SegmentIndex = 0
        };
        var artifacts = new ConversionArtifacts();
        artifacts.Images.Add(image);
        var segments = new List<DocumentSegment> { new(placeholder, SegmentType.Image) };
        var pipeline = new ConversionPipeline(
            [new AiImageEnrichmentMiddleware()],
            new StaticAiModelProvider(new RecordingChatClient(description), null),
            logger: null,
            SegmentOptions.Default,
            ProgressDetailLevel.Basic);

        await pipeline.ExecuteAsync(new StreamInfo(extension: ".png"), artifacts, segments, CancellationToken.None);

        image.DetailedDescription.ShouldBe(description);
        segments.Count.ShouldBe(1);
        segments[0].Markdown.ShouldContain($"<!-- Image description:{Environment.NewLine}{description}{Environment.NewLine}");
    }

    [Fact]
    public async Task ImageEnrichment_MissingMimeFailsBeforeProviderInvocation()
    {
        var chat = new RecordingChatClient("description");
        var artifacts = new ConversionArtifacts();
        artifacts.Images.Add(new ImageArtifact([1]));
        var pipeline = CreateImagePipeline(chat);

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.ExecuteAsync(
            new StreamInfo(extension: ".png"), artifacts, new List<DocumentSegment>(), CancellationToken.None));

        chat.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ImageEnrichment_NonPositiveParallelismFailsDuringConfiguration(int parallelism)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SegmentOptions.Default with { MaxParallelImageAnalysis = parallelism });
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task ImageEnrichment_ProviderAuthorizationFailurePropagates(int status)
    {
        var failure = new HttpRequestException("Provider denied access.", null, (System.Net.HttpStatusCode)status);
        var chat = new RecordingChatClient("", failure);
        var artifacts = new ConversionArtifacts();
        artifacts.Images.Add(new ImageArtifact([1], ManagedCode.MimeTypes.MimeHelper.PNG));

        var actual = await Assert.ThrowsAsync<HttpRequestException>(() => CreateImagePipeline(chat).ExecuteAsync(
            new StreamInfo(extension: ".png"), artifacts, new List<DocumentSegment>(), CancellationToken.None));

        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task ImageEnrichment_WrappedProviderCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        var chat = new RecordingChatClient("", new InvalidOperationException("Wrapped provider cancellation."), cancellation);
        var artifacts = new ConversionArtifacts();
        artifacts.Images.Add(new ImageArtifact([1], ManagedCode.MimeTypes.MimeHelper.PNG));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateImagePipeline(chat).ExecuteAsync(
            new StreamInfo(extension: ".png"), artifacts, new List<DocumentSegment>(), cancellation.Token));
    }

    [Fact]
    public async Task ImageEnrichment_OptionalProviderFailureKeepsOriginalSegment()
    {
        var chat = new RecordingChatClient("", new HttpRequestException("Provider temporarily unavailable.", null, System.Net.HttpStatusCode.ServiceUnavailable));
        var artifacts = new ConversionArtifacts();
        artifacts.Images.Add(new ImageArtifact([1], ManagedCode.MimeTypes.MimeHelper.PNG));
        var segment = new DocumentSegment("Original image", SegmentType.Image);
        var segments = new List<DocumentSegment> { segment };

        await CreateImagePipeline(chat).ExecuteAsync(new StreamInfo(extension: ".png"), artifacts, segments, CancellationToken.None);

        Assert.Same(segment, Assert.Single(segments));
    }

    private static ConversionPipeline CreateImagePipeline(IChatClient chat, SegmentOptions? options = null)
        => new([new AiImageEnrichmentMiddleware()], new StaticAiModelProvider(chat, null), null,
            options ?? SegmentOptions.Default, ProgressDetailLevel.Basic);

    [Fact]
    public async Task ImageEnrichment_UsesInjectedChatClient()
    {
        var chatClient = new RecordingChatClient("SONAR diagram with layered services");
        var options = new MarkItDownOptions
        {
            AiModels = new StaticAiModelProvider(chatClient, null),
            EnableAiImageEnrichment = true
        };

        var client = new MarkItDownClient(options);

        await using var stream = DocxInlineImageFactory.Create();
        var streamInfo = new StreamInfo(
            mimeType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            extension: ".docx",
            fileName: "inline-images.docx");

        var result = await client.ConvertAsync(stream, streamInfo);

        chatClient.Requests.Count.ShouldBeGreaterThan(0);
        result.Artifacts.Images.ShouldNotBeEmpty();
        result.Artifacts.Images[0].DetailedDescription.ShouldBe("SONAR diagram with layered services");
    }

    [Fact]
    public async Task CustomMiddleware_UsesSpeechToTextClient()
    {
        var speechClient = new RecordingSpeechClient("Mission control acknowledges receipt of telemetry.");
        var invoked = false;
        var pipeline = new ConversionPipeline(
            new IConversionMiddleware[] { new SpeechAnnotationMiddleware(new byte[] { 1, 2, 3 }, () => invoked = true) },
            new StaticAiModelProvider(null, speechClient),
            logger: null,
            SegmentOptions.Default,
            ProgressDetailLevel.Basic);

        var artifacts = new ConversionArtifacts();
        var segments = new List<DocumentSegment>();
        var streamInfo = new StreamInfo(mimeType: "audio/wav", extension: ".wav", fileName: "sample.wav");

        await pipeline.ExecuteAsync(streamInfo, artifacts, segments, CancellationToken.None);

        invoked.ShouldBeTrue();
        speechClient.InvocationCount.ShouldBe(1);
        segments.ShouldContain(segment =>
            segment.Type == SegmentType.Audio &&
            segment.Markdown.Contains("Mission control acknowledges receipt of telemetry."));
    }

    private sealed class RecordingChatClient(string responseText, Exception? failure = null, CancellationTokenSource? cancellation = null) : IChatClient
    {
        public List<IReadOnlyList<ChatMessage>> Requests { get; } = new();

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken = default)
        {
            var recorded = new List<ChatMessage>(messages);
            Requests.Add(recorded);
            cancellation?.Cancel();
            if (failure is not null)
            {
                throw failure;
            }

            var payload = JsonSerializer.Serialize(new { description = responseText });
            var reply = new ChatMessage(ChatRole.Assistant, payload);
            return Task.FromResult(new ChatResponse(new List<ChatMessage> { reply }));
        }

        IAsyncEnumerable<ChatResponseUpdate> IChatClient.GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingSpeechClient(string transcript) : ISpeechToTextClient
    {
        public int InvocationCount { get; private set; }

        public Task<SpeechToTextResponse> GetTextAsync(System.IO.Stream audio, SpeechToTextOptions? options, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(new SpeechToTextResponse(transcript));
        }

        IAsyncEnumerable<SpeechToTextResponseUpdate> ISpeechToTextClient.GetStreamingTextAsync(System.IO.Stream audio, SpeechToTextOptions? options, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class SpeechAnnotationMiddleware(byte[] audioBytes, Action onInvoke) : IConversionMiddleware
    {
        public async Task InvokeAsync(ConversionPipelineContext context, CancellationToken cancellationToken)
        {
            onInvoke();
            var speechClient = context.AiModels.SpeechToTextClient;
            if (speechClient is null || audioBytes.Length == 0)
            {
                return;
            }

            await using var buffer = new System.IO.MemoryStream(audioBytes, writable: false);
            var response = await speechClient.GetTextAsync(buffer, new SpeechToTextOptions
            {
                ModelId = "gpt-4o-transcribe",
                SpeechLanguage = "en-US"
            }, cancellationToken);

            if (string.IsNullOrWhiteSpace(response.Text))
            {
                return;
            }

            context.Segments.Add(new DocumentSegment(
                markdown: response.Text!,
                type: SegmentType.Audio,
                label: "AI Speech Transcript"));
        }
    }
}
#pragma warning restore MEAI001
