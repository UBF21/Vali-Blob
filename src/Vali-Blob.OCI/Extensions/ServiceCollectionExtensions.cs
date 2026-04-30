using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Oci.Common;
using Oci.Common.Auth;
using Oci.ObjectstorageService;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Options;

namespace ValiBlob.OCI.Extensions;

/// <summary>
/// Dependency injection extensions for Oracle Cloud Infrastructure Object Storage provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OCI Object Storage provider with the ValiBlob dependency injection container.
    /// </summary>
    /// <param name="builder">The ValiStorageBuilder instance.</param>
    /// <param name="configure">Optional configuration action for OCIStorageOptions.</param>
    /// <returns>The ValiStorageBuilder instance for method chaining.</returns>
    public static ValiStorageBuilder UseOCI(
        this ValiStorageBuilder builder,
        Action<OCIStorageOptions>? configure = null)
    {
        builder.Services.AddOptions<OCIStorageOptions>()
            .BindConfiguration(OCIStorageOptions.SectionName);

        if (configure is not null)
            builder.Services.Configure(configure);

        builder.Services.TryAddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<OCIStorageOptions>>().Value;
            IBasicAuthenticationDetailsProvider authProvider;

            if (opts.TenancyId is not null && opts.UserId is not null &&
                opts.Fingerprint is not null && opts.PrivateKeyPath is not null)
            {
                authProvider = new SimpleAuthenticationDetailsProvider
                {
                    TenantId = opts.TenancyId,
                    UserId = opts.UserId,
                    Fingerprint = opts.Fingerprint,
                    Region = Region.FromRegionId(opts.Region),
                    PrivateKeySupplier = new FilePrivateKeySupplier(opts.PrivateKeyPath, null!)
                };
            }
            else
            {
                authProvider = new ConfigFileAuthenticationDetailsProvider("DEFAULT");
            }

            return new ObjectStorageClient(authProvider);
        });

        builder.Services.AddKeyedScoped<IStorageProvider, OCIStorageProvider>(nameof(StorageProviderType.OCI));

        return builder;
    }
}
