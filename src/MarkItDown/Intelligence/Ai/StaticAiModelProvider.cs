// Suppress MEAI001 preview warning for consumers opting into Microsoft.Extensions.AI preview APIs.
#pragma warning disable MEAI001
using Microsoft.Extensions.AI;

namespace MarkItDown.Intelligence;

/// <summary>
/// Simple <see cref="IAiModelProvider"/> implementation that returns the supplied clients.
/// </summary>
public sealed class StaticAiModelProvider(IChatClient? chatClient, ISpeechToTextClient? speechToTextClient) : IAiModelProvider
{
    public IChatClient? ChatClient { get; } = chatClient;

    public ISpeechToTextClient? SpeechToTextClient { get; } = speechToTextClient;
}
#pragma warning restore MEAI001
