using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown;

namespace MarkItDown.Tests;

internal sealed class RecordingPipeline : IConversionPipeline
{
    private readonly string message;

    public RecordingPipeline(string message = "Pipeline")
    {
        this.message = message;
    }

    public bool Executed { get; private set; }

    public Task ExecuteAsync(StreamInfo streamInfo, ConversionArtifacts artifacts, IList<DocumentSegment> segments, CancellationToken cancellationToken)
    {
        Executed = true;

        if (artifacts.Images.Count > 0)
        {
            var artifact = artifacts.Images[0];
            if (artifact.SegmentIndex is int index && index >= 0 && index < segments.Count)
            {
                var segment = segments[index];
                var updatedMarkdown = InjectAfterPlaceholder(segment.Markdown, artifact.PlaceholderMarkdown, Environment.NewLine + message);
                var updatedSegment = new DocumentSegment(
                    updatedMarkdown,
                    segment.Type,
                    segment.Number,
                    segment.Label,
                    segment.StartTime,
                    segment.EndTime,
                    segment.Source,
                    segment.AdditionalMetadata);
                segments[index] = updatedSegment;
                artifact.DetailedDescription = message;
            }
        }

        return Task.CompletedTask;
    }

    private static string InjectAfterPlaceholder(string markdown, string? placeholder, string injection)
    {
        if (string.IsNullOrWhiteSpace(placeholder))
        {
            return markdown + injection;
        }

        var index = markdown.IndexOf(placeholder, StringComparison.Ordinal);
        if (index < 0)
        {
            return markdown + injection;
        }

        var insertPosition = index + placeholder.Length;
        return markdown.Insert(insertPosition, injection);
    }
}
