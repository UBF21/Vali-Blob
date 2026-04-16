using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;

namespace ValiBlob.Testing.Extensions;

public static class ServiceCollectionExtensions
{
    public static ValiStorageBuilder UseInMemory(this ValiStorageBuilder builder)
    {
        builder.Services.TryAddSingleton<InMemoryStorageProvider>();
        builder.Services.AddKeyedSingleton<IStorageProvider, InMemoryStorageProvider>("InMemory");
        builder.Services.Configure<Core.Options.StorageGlobalOptions>(o => o.DefaultProvider = "InMemory");
        return builder;
    }
}
