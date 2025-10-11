using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MarkItDown;

/// <summary>
/// Represents a middleware component that can inspect or modify conversion artifacts before Markdown composition.
/// </summary>
public interface IConversionMiddleware
{
    /// <summary>
    /// Invoked for each conversion with the extracted artifacts and mutable segment list.
    /// </summary>
    Task InvokeAsync(ConversionPipelineContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Abstraction for executing a middleware pipeline.
/// </summary>
public interface IConversionPipeline
{
    Task ExecuteAsync(StreamInfo streamInfo, ConversionArtifacts artifacts, IList<DocumentSegment> segments, CancellationToken cancellationToken);
}
