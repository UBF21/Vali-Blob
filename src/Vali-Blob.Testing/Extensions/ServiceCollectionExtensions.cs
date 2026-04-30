using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Options;

namespace ValiBlob.Testing.Extensions;

public static class ServiceCollectionExtensions
{
    public static ValiStorageBuilder UseInMemory(this ValiStorageBuilder builder)
    {
        builder.Services.TryAddSingleton<InMemoryStorageProvider>();
        builder.Services.AddKeyedSingleton<IStorageProvider, InMemoryStorageProvider>(nameof(StorageProviderType.InMemory));
        builder.Services.Configure<StorageGlobalOptions>(o => o.DefaultProvider = nameof(StorageProviderType.InMemory));
        return builder;
    }
}
