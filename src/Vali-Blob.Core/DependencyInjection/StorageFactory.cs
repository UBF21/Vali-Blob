using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Events;
using ValiBlob.Core.Options;
using ValiBlob.Core.Providers;

namespace ValiBlob.Core.DependencyInjection;

public sealed class StorageFactory : IStorageFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly StorageGlobalOptions _options;

    public StorageFactory(IServiceProvider serviceProvider, IOptions<StorageGlobalOptions> options)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }

    public IStorageProvider Create(string? providerName = null)
    {
        var key = providerName ?? _options.DefaultProvider;

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "No provider specified and no DefaultProvider configured in ValiBlob options.");

        var provider = _serviceProvider.GetKeyedService<IStorageProvider>(key);

        if (provider is null)
            throw new InvalidOperationException(
                $"No storage provider registered with name '{key}'. " +
                $"Make sure to call .Use{key}() during setup.");

        return ApplyDecorators(provider);
    }

    public IStorageProvider Create<TProvider>() where TProvider : IStorageProvider
    {
        var typeName = typeof(TProvider).Name;
        var keyedProvider = _serviceProvider.GetKeyedService<IStorageProvider>(typeName);
        var provider = keyedProvider ?? _serviceProvider.GetRequiredService<TProvider>();
        return ApplyDecorators(provider);
    }

    public IEnumerable<IStorageProvider> GetAll()
    {
        if (_serviceProvider is IKeyedServiceProvider keyedProvider)
        {
            var providers = new List<IStorageProvider>();

            foreach (var providerKey in new[] { "Local", "InMemory", "AWS", "Azure", "GCP", "OCI", "Supabase" })
            {
                var provider = _serviceProvider.GetKeyedService<IStorageProvider>(providerKey);
                if (provider is not null)
                    providers.Add(ApplyDecorators(provider));
            }

            return providers;
        }

        return _serviceProvider.GetServices<IStorageProvider>();
    }

    private IStorageProvider ApplyDecorators(IStorageProvider provider)
    {
        var result = provider;

        if (_options.ApplyTelemetryDecorator)
            result = new StorageTelemetryDecorator(result);

        if (_options.ApplyEventDecorator)
        {
            var dispatcher = _serviceProvider.GetService<IStorageEventDispatcher>();
            if (dispatcher is not null)
                result = new StorageEventDecorator(result, dispatcher);
        }

        return result;
    }
}
