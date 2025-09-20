namespace MarkItDown.Core;

/// <summary>
/// Configuration for Azure Document Intelligence powered conversions.
/// </summary>
public sealed record DocumentIntelligenceOptions
{
    /// <summary>
    /// Gets the endpoint URI of the Document Intelligence service. Required when enabling the converter.
    /// </summary>
    public required string Endpoint { get; init; }

    /// <summary>
    /// Gets or sets the credential object that should be passed to the Azure SDK. The underlying converter will
    /// validate the type at runtime to avoid introducing a direct dependency on Azure Identity packages.
    /// </summary>
    public object? Credential { get; init; }

    /// <summary>
    /// Gets or sets the API version to use. Falls back to the service default when not specified.
    /// </summary>
    public string? ApiVersion { get; init; }

    /// <summary>
    /// Restrict conversions to the provided set of file types. When <see langword="null"/> the converter uses the
    /// service defaults.
    /// </summary>
    public IReadOnlyCollection<string>? FileTypes { get; init; }
}
