using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Local.Options;

namespace ValiBlob.Local.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the local filesystem storage provider with explicit configuration.
    /// </summary>
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

        builder.Services.AddKeyedScoped<IStorageProvider, LocalStorageProvider>("Local");
    }
}
