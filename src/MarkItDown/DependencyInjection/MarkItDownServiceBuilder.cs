using MarkItDown.Converters;
using Microsoft.Extensions.DependencyInjection;

namespace MarkItDown.DependencyInjection;

/// <summary>
/// Configures MarkItDown services when registered with dependency injection.
/// </summary>
public sealed class MarkItDownServiceBuilder
{
    internal MarkItDownServiceBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Gets the underlying service collection.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Applies additional configuration to <see cref="MarkItDownOptions"/>.
    /// </summary>
    public MarkItDownServiceBuilder Configure(Action<MarkItDownOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Registers a custom converter as part of the service collection.
    /// </summary>
    public MarkItDownServiceBuilder AddConverter<TConverter>() where TConverter : DocumentConverterBase
    {
        Services.AddSingleton<DocumentConverterBase, TConverter>();
        return this;
    }

    /// <summary>
    /// Registers a custom converter using a factory delegate.
    /// </summary>
    public MarkItDownServiceBuilder AddConverter<TConverter>(Func<IServiceProvider, TConverter> factory) where TConverter : DocumentConverterBase
    {
        ArgumentNullException.ThrowIfNull(factory);
        Services.AddSingleton<DocumentConverterBase>(factory);
        return this;
    }

    /// <summary>
    /// Registers a conversion middleware component that will be included in the pipeline.
    /// </summary>
    public MarkItDownServiceBuilder AddMiddleware<TMiddleware>() where TMiddleware : class, IConversionMiddleware
    {
        Services.AddSingleton<IConversionMiddleware, TMiddleware>();
        return this;
    }

    /// <summary>
    /// Registers a conversion middleware using a factory.
    /// </summary>
    public MarkItDownServiceBuilder AddMiddleware<TMiddleware>(Func<IServiceProvider, TMiddleware> factory) where TMiddleware : class, IConversionMiddleware
    {
        ArgumentNullException.ThrowIfNull(factory);
        Services.AddSingleton<IConversionMiddleware>(factory);
        return this;
    }
}
