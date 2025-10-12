using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;
using MarkItDown.Intelligence;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace MarkItDown.Tests.Intelligence;

public class ImageChatEnricherTests
{
    [Fact]
    public async Task EnrichAsync_AttachesImageBinaryToChatMessage()
    {
        // Arrange
        var image = new ImageArtifact(new byte[] { 0x1, 0x2, 0x3 }, "image/png", pageNumber: 7, source: "page-7.png");
        var streamInfo = new StreamInfo(fileName: "document.docx");
        var responsePayload = JsonSerializer.Serialize(new
        {
            description = "Dashboard overview with KPI cards.",
            textRegions = new[]
            {
                new { label = "Heading", text = "Dashboard Overview" }
            },
            diagrams = Array.Empty<object>(),
            charts = Array.Empty<object>(),
            tables = Array.Empty<object>(),
            layoutRegions = Array.Empty<object>(),
            uiElements = Array.Empty<object>(),
            highlights = Array.Empty<object>(),
            notes = Array.Empty<string>()
        });

        var chatClient = new RecordingChatClient(responsePayload);

        // Act
        var result = await ImageChatEnricher.EnrichAsync(image, streamInfo, chatClient, NullLogger.Instance, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result!.HasInsight.ShouldBeTrue();

        var request = chatClient.SingleRequest;
        request.Count.ShouldBe(2); // system + user message

        var systemMessage = request[0];
        systemMessage.Role.ShouldBe(ChatRole.System);
        systemMessage.Contents.OfType<DataContent>().ShouldBeEmpty();
        var systemInstruction = systemMessage.Contents.OfType<TextContent>().SingleOrDefault();
        systemInstruction.ShouldNotBeNull();
        systemInstruction!.Text.ShouldContain("Document Image Intelligence Analyzer");

        var userMessage = request[1];
        userMessage.Role.ShouldBe(ChatRole.User);
        var attachments = userMessage.Contents.OfType<DataContent>().ToList();
        attachments.Count.ShouldBe(1);
        attachments[0].MediaType.ShouldBe("image/png");
        attachments[0].Data.Length.ShouldBe(image.Data.Length);
    }

    private sealed class RecordingChatClient(string payload) : IChatClient
    {
        private readonly List<IReadOnlyList<ChatMessage>> requests = new();

        public IReadOnlyList<ChatMessage> SingleRequest => requests.Count switch
        {
            1 => requests[0],
            _ => throw new InvalidOperationException("Expected exactly one chat request.")
        };

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken = default)
        {
            requests.Add(messages.ToList());
            var reply = new ChatMessage(ChatRole.Assistant, payload);
            return Task.FromResult(new ChatResponse(new List<ChatMessage> { reply }));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
