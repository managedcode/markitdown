using System.Linq;
using System.Net.Http;
using MarkItDown.Converters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarkItDown.DependencyInjection;

/// <summary>
/// Extension methods for registering MarkItDown with <see cref="IServiceCollection"/>.
/// </summary>
public static class MarkItDownServiceCollectionExtensions
{
    /// <summary>
    /// Adds the <see cref="MarkItDownClient"/> and related services to the dependency injection container.
    /// </summary>
    public static MarkItDownServiceBuilder AddMarkItDown(this IServiceCollection services, Action<MarkItDownServiceBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<MarkItDownOptions>();

        services.TryAddSingleton<MarkItDownClient>(sp =>
        {
            var optionsMonitor = sp.GetRequiredService<IOptions<MarkItDownOptions>>();
            var options = optionsMonitor.Value;

            var logger = sp.GetService<ILogger<MarkItDownClient>>();
            var httpClient = sp.GetService<HttpClient>();

            var middleware = sp.GetServices<IConversionMiddleware>().ToList();
            if (middleware.Count > 0)
            {
                options.ConversionMiddleware = options.ConversionMiddleware.Concat(middleware).ToArray();
            }

            var client = new MarkItDownClient(options, logger, httpClient);
            foreach (var converter in sp.GetServices<DocumentConverterBase>())
            {
                client.RegisterConverter(converter);
            }

            return client;
        });

        services.TryAddSingleton<IMarkItDownClient>(sp => sp.GetRequiredService<MarkItDownClient>());

        var builder = new MarkItDownServiceBuilder(services);
        configure?.Invoke(builder);
        return builder;
    }
}
