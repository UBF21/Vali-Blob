using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Options;

namespace ValiBlob.Azure.Extensions;

/// <summary>
/// Extension methods for configuring Azure Blob Storage with ValiBlob.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Azure Blob Storage as the configured storage provider.
    /// </summary>
    /// <param name="builder">The ValiBlob storage builder.</param>
    /// <param name="configure">Optional action to configure Azure Blob Storage options.</param>
    /// <returns>The configured ValiBlob storage builder for method chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when neither ConnectionString nor AccountName+AccountKey are provided.</exception>
    public static ValiStorageBuilder UseAzure(
        this ValiStorageBuilder builder,
        Action<AzureBlobOptions>? configure = null)
    {
        builder.Services.AddOptions<AzureBlobOptions>()
            .BindConfiguration(AzureBlobOptions.SectionName);

        if (configure is not null)
            builder.Services.Configure(configure);

        builder.Services.TryAddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureBlobOptions>>().Value;

            if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
                return new BlobServiceClient(opts.ConnectionString);

            if (!string.IsNullOrWhiteSpace(opts.AccountName) && !string.IsNullOrWhiteSpace(opts.AccountKey))
            {
                var credential = new StorageSharedKeyCredential(opts.AccountName, opts.AccountKey);
                var serviceUrl = opts.ServiceUrl ?? $"https://{opts.AccountName}.blob.core.windows.net";
                return new BlobServiceClient(new Uri(serviceUrl), credential);
            }

            throw new InvalidOperationException(
                "ValiBlob Azure: provide either ConnectionString or AccountName + AccountKey.");
        });

        builder.Services.AddKeyedScoped<IStorageProvider, AzureBlobProvider>(nameof(StorageProviderType.Azure));

        return builder;
    }
}
