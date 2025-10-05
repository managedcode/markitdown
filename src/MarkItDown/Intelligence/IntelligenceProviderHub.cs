namespace MarkItDown.Intelligence;

/// <summary>
/// Aggregates the optional intelligence providers configured for the current <see cref="MarkItDown"/> instance.
/// </summary>
public sealed class IntelligenceProviderHub
{
    public IntelligenceProviderHub(
        IDocumentIntelligenceProvider? document,
        IImageUnderstandingProvider? image,
        IMediaTranscriptionProvider? media,
        IAiModelProvider? aiModels)
    {
        Document = document;
        Image = image;
        Media = media;
        AiModels = aiModels;
    }

    public IDocumentIntelligenceProvider? Document { get; }

    public IImageUnderstandingProvider? Image { get; }

    public IMediaTranscriptionProvider? Media { get; }

    public IAiModelProvider? AiModels { get; }
}
