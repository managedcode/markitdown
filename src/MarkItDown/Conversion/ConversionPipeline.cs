using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MarkItDown.Intelligence;
using Microsoft.Extensions.Logging;

namespace MarkItDown;

/// <summary>
/// Sequential middleware pipeline that executes configured <see cref="IConversionMiddleware"/> components.
/// </summary>
public sealed class ConversionPipeline : IConversionPipeline
{
    private readonly IReadOnlyList<IConversionMiddleware> middlewares;
    private readonly IAiModelProvider aiModels;
    private readonly ILogger? logger;

    public static IConversionPipeline Empty { get; } = new ConversionPipeline(Array.Empty<IConversionMiddleware>(), NullAiModelProvider.Instance, logger: null);

    public ConversionPipeline(IEnumerable<IConversionMiddleware> middlewares, IAiModelProvider aiModels, ILogger? logger)
    {
        this.middlewares = (middlewares ?? throw new ArgumentNullException(nameof(middlewares))).ToArray();
        this.aiModels = aiModels ?? NullAiModelProvider.Instance;
        this.logger = logger;
    }

    public async Task ExecuteAsync(StreamInfo streamInfo, ConversionArtifacts artifacts, IList<DocumentSegment> segments, CancellationToken cancellationToken)
    {
        if (middlewares.Count == 0)
        {
            return;
        }

        var context = new ConversionPipelineContext(streamInfo, artifacts, segments, aiModels, logger);
        foreach (var middleware in middlewares)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await middleware.InvokeAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Conversion middleware {Middleware} failed", middleware.GetType().Name);
            }
        }
    }
}
