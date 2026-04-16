using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Events;
using ValiBlob.Core.Migration;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Resumable;

namespace ValiBlob.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static ValiStorageBuilder AddValiBlob(
        this IServiceCollection services,
        Action<StorageGlobalOptions>? configure = null)
    {
        services.AddOptions<StorageGlobalOptions>()
            .BindConfiguration(StorageGlobalOptions.SectionName);

        if (configure is not null)
            services.Configure(configure);

        services.AddOptions<ResilienceOptions>()
            .BindConfiguration($"{StorageGlobalOptions.SectionName}:Resilience");

        services.AddOptions<ValidationOptions>()
            .BindConfiguration($"{StorageGlobalOptions.SectionName}:Validation");

        services.AddOptions<CompressionOptions>()
            .BindConfiguration($"{StorageGlobalOptions.SectionName}:Compression");

        services.AddOptions<EncryptionOptions>()
            .BindConfiguration($"{StorageGlobalOptions.SectionName}:Encryption");

        services.AddOptions<ResumableUploadOptions>()
            .BindConfiguration(ResumableUploadOptions.SectionName);

        services.TryAddSingleton<IResumableSessionStore, InMemoryResumableSessionStore>();

        services.TryAddSingleton<StoragePipelineBuilder>(sp =>
        {
            var middlewares = sp.GetServices<IStorageMiddleware>();
            var builder = new StoragePipelineBuilder();
            foreach (var middleware in middlewares)
                builder.Use(middleware);
            return builder;
        });

        services.TryAddSingleton<IStorageFactory, StorageFactory>();
        services.TryAddSingleton<StorageEventDispatcher>();
        services.TryAddSingleton<IStorageMigrator, StorageMigrator>();

        return new ValiStorageBuilder(services);
    }
}
