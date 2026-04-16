using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;

namespace ValiBlob.AWS.Extensions;

public static class ServiceCollectionExtensions
{
    public static ValiStorageBuilder UseAWS(
        this ValiStorageBuilder builder,
        Action<AWSS3Options>? configure = null)
    {
        builder.Services.AddOptions<AWSS3Options>()
            .BindConfiguration(AWSS3Options.SectionName);

        if (configure is not null)
            builder.Services.Configure(configure);

        builder.Services.TryAddSingleton<IAmazonS3>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AWSS3Options>>().Value;

            var config = new AmazonS3Config
            {
                RegionEndpoint = string.IsNullOrWhiteSpace(opts.Region)
                    ? RegionEndpoint.USEast1
                    : RegionEndpoint.GetBySystemName(opts.Region),
                ForcePathStyle = opts.ForcePathStyle
            };

            if (!string.IsNullOrWhiteSpace(opts.ServiceUrl))
            {
                config.ServiceURL = opts.ServiceUrl;
                config.ForcePathStyle = true;
            }

            if (!opts.UseIAMRole
                && !string.IsNullOrWhiteSpace(opts.AccessKeyId)
                && !string.IsNullOrWhiteSpace(opts.SecretAccessKey))
            {
                var credentials = new BasicAWSCredentials(opts.AccessKeyId, opts.SecretAccessKey);
                return new AmazonS3Client(credentials, config);
            }

            return new AmazonS3Client(config);
        });

        builder.Services.AddKeyedScoped<IStorageProvider, AWSS3Provider>("AWS");

        return builder;
    }

    public static ValiStorageBuilder UseMinIO(
        this ValiStorageBuilder builder,
        Action<AWSS3Options> configure)
    {
        return builder.UseAWS(opts =>
        {
            configure(opts);
            opts.ForcePathStyle = true;
        });
    }
}
