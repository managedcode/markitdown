// Microsoft.Extensions.AI currently surfaces preview APIs that emit MEAI001. Suppress the warning so consumers can opt-in via options.
#pragma warning disable MEAI001
using Microsoft.Extensions.AI;

namespace MarkItDown.Intelligence;

/// <summary>
/// Exposes Microsoft.Extensions.AI clients that can be leveraged by converters/providers for advanced reasoning tasks.
/// </summary>
public interface IAiModelProvider
{
    IChatClient? ChatClient { get; }

    ISpeechToTextClient? SpeechToTextClient { get; }
}

internal sealed class NullAiModelProvider : IAiModelProvider
{
    public static IAiModelProvider Instance { get; } = new NullAiModelProvider();

    private NullAiModelProvider()
    {
    }

    public IChatClient? ChatClient => null;

    public ISpeechToTextClient? SpeechToTextClient => null;
}
#pragma warning restore MEAI001
