using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Options;
using ValiBlob.Local.Options;

namespace ValiBlob.Local.DependencyInjection;

/// <summary>
/// Dependency injection extensions for local filesystem storage provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the local filesystem storage provider with explicit configuration.
    /// </summary>
    /// <param name="builder">The ValiStorageBuilder instance.</param>
    /// <param name="configure">Configuration action for LocalStorageOptions.</param>
    /// <returns>The ValiStorageBuilder for chaining.</returns>
    public static ValiStorageBuilder UseLocal(
        this ValiStorageBuilder builder,
        Action<LocalStorageOptions> configure)
    {
        builder.Services.Configure(configure);
        RegisterLocal(builder);
        return builder;
    }

    /// <summary>
    /// Registers the local filesystem storage provider bound to a configuration section.
    /// </summary>
    /// <param name="builder">The ValiStorageBuilder instance.</param>
    /// <param name="configuration">The configuration object.</param>
    /// <param name="sectionName">The configuration section name. Defaults to "ValiBlob:Local".</param>
    /// <returns>The ValiStorageBuilder for chaining.</returns>
    public static ValiStorageBuilder UseLocal(
        this ValiStorageBuilder builder,
        IConfiguration configuration,
        string sectionName = "ValiBlob:Local")
    {
        builder.Services.Configure<LocalStorageOptions>(configuration.GetSection(sectionName));
        RegisterLocal(builder);
        return builder;
    }

    private static void RegisterLocal(ValiStorageBuilder builder)
    {
        // Eagerly create BasePath if configured
        builder.Services.AddSingleton<LocalStorageProvider>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LocalStorageOptions>>().Value;
            if (opts.CreateIfNotExists && !string.IsNullOrEmpty(opts.BasePath))
                System.IO.Directory.CreateDirectory(opts.BasePath);

            return ActivatorUtilities.CreateInstance<LocalStorageProvider>(sp);
        });

        builder.Services.AddKeyedScoped<IStorageProvider, LocalStorageProvider>(nameof(StorageProviderType.Local));
    }
}
