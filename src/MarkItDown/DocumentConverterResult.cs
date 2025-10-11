using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MarkItDown;

/// <summary>
/// The result of converting a document to Markdown.
/// </summary>
public sealed class DocumentConverterResult
{
    private static readonly IReadOnlyList<DocumentSegment> EmptySegments =
        new ReadOnlyCollection<DocumentSegment>(Array.Empty<DocumentSegment>());

    /// <summary>
    /// Initialize the DocumentConverterResult.
    /// </summary>
    /// <param name="markdown">The converted Markdown text.</param>
    /// <param name="title">Optional title of the document.</param>
    /// <param name="segments">Optional collection of segments that represent structured slices of the output.</param>
    /// <param name="artifacts">Optional raw extraction artifacts available prior to Markdown composition.</param>
    public DocumentConverterResult(string markdown, string? title = null, IReadOnlyList<DocumentSegment>? segments = null, ConversionArtifacts? artifacts = null)
    {
        Markdown = markdown ?? throw new ArgumentNullException(nameof(markdown));
        Title = title;
        Segments = segments is null
            ? EmptySegments
            : new ReadOnlyCollection<DocumentSegment>(segments.ToArray());
        Artifacts = artifacts ?? ConversionArtifacts.Empty;
    }

    /// <summary>
    /// The converted Markdown text.
    /// </summary>
    public string Markdown { get; }

    /// <summary>
    /// Optional title of the document.
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// Segmented view of the converted output.
    /// </summary>
    public IReadOnlyList<DocumentSegment> Segments { get; }

    /// <summary>
    /// Raw extraction artifacts captured during conversion.
    /// </summary>
    public ConversionArtifacts Artifacts { get; }

    /// <summary>
    /// Soft-deprecated alias for Markdown. New code should migrate to using Markdown property.
    /// </summary>
    public string TextContent => Markdown;

    /// <summary>
    /// Return the converted Markdown text.
    /// </summary>
    public override string ToString() => Markdown;
}
