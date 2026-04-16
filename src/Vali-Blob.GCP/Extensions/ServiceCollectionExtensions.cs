using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;

namespace ValiBlob.GCP.Extensions;

public static class ServiceCollectionExtensions
{
    public static ValiStorageBuilder UseGCP(
        this ValiStorageBuilder builder,
        Action<GCPStorageOptions>? configure = null)
    {
        builder.Services.AddOptions<GCPStorageOptions>()
            .BindConfiguration(GCPStorageOptions.SectionName);

        if (configure is not null)
            builder.Services.Configure(configure);

        builder.Services.TryAddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<GCPStorageOptions>>().Value;

            if (opts.CredentialsPath is not null)
            {
                var credential = GoogleCredential.FromFile(opts.CredentialsPath);
                return StorageClient.Create(credential);
            }

            if (opts.CredentialsJson is not null)
            {
                var credential = GoogleCredential.FromJson(opts.CredentialsJson);
                return StorageClient.Create(credential);
            }

            return StorageClient.Create(); // Use Application Default Credentials
        });

        builder.Services.TryAddSingleton<GCPResumableBuffer>();
        builder.Services.AddKeyedScoped<IStorageProvider, GCPStorageProvider>("GCP");

        return builder;
    }
}
