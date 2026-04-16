using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline.Middlewares;

namespace ValiBlob.Core.DependencyInjection;

public sealed class PipelineConfigurator
{
    private readonly IServiceCollection _services;

    public PipelineConfigurator(IServiceCollection services) => _services = services;

    public PipelineConfigurator UseValidation(Action<ValidationOptions>? configure = null)
    {
        if (configure is not null)
            _services.Configure(configure);

        _services.TryAddEnumerable(ServiceDescriptor.Transient<IStorageMiddleware, ValidationMiddleware>());
        return this;
    }

    public PipelineConfigurator UseCompression(Action<CompressionOptions>? configure = null)
    {
        if (configure is not null)
            _services.Configure(configure);

        _services.TryAddEnumerable(ServiceDescriptor.Transient<IStorageMiddleware, CompressionMiddleware>());
        return this;
    }

    public PipelineConfigurator UseEncryption(Action<EncryptionOptions>? configure = null)
    {
        if (configure is not null)
            _services.Configure(configure);

        _services.TryAddEnumerable(ServiceDescriptor.Transient<IStorageMiddleware, EncryptionMiddleware>());
        return this;
    }

    public PipelineConfigurator Use<TMiddleware>() where TMiddleware : class, IStorageMiddleware
    {
        _services.TryAddEnumerable(ServiceDescriptor.Transient<IStorageMiddleware, TMiddleware>());
        return this;
    }
}
