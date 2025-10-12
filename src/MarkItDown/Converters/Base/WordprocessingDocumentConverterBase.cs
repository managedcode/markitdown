using System;
using DocumentFormat.OpenXml.Packaging;

namespace MarkItDown.Converters;

/// <summary>
/// Base converter for formats backed by <see cref="WordprocessingDocument"/> packages.
/// </summary>
public abstract class WordprocessingDocumentConverterBase : DocumentPipelineConverterBase
{
    protected WordprocessingDocumentConverterBase(int priority)
        : base(priority)
    {
    }

    protected static WordprocessingDocument OpenWordprocessingDocument(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must be provided.", nameof(path));
        }

        var settings = new OpenSettings
        {
            AutoSave = false
        };

        return WordprocessingDocument.Open(path, isEditable: false, settings);
    }
}
