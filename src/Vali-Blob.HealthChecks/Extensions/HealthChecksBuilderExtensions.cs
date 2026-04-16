using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ValiBlob.Core.Abstractions;

namespace ValiBlob.HealthChecks.Extensions;

public static class HealthChecksBuilderExtensions
{
    /// <summary>
    /// Adds a health check for the default ValiBlob storage provider.
    /// </summary>
    public static IHealthChecksBuilder AddValiBlob(
        this IHealthChecksBuilder builder,
        string? name = null,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        Action<StorageHealthCheckOptions>? configure = null)
    {
        var options = new StorageHealthCheckOptions();
        configure?.Invoke(options);

        return builder.Add(new HealthCheckRegistration(
            name ?? "valiblob",
            sp =>
            {
                var factory = sp.GetRequiredService<IStorageFactory>();
                var provider = factory.Create();
                return new StorageProviderHealthCheck(provider, options);
            },
            failureStatus,
            tags));
    }

    /// <summary>
    /// Adds a health check for a specific named ValiBlob storage provider.
    /// </summary>
    public static IHealthChecksBuilder AddValiBlob(
        this IHealthChecksBuilder builder,
        string providerName,
        string? checkName = null,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        Action<StorageHealthCheckOptions>? configure = null)
    {
        var options = new StorageHealthCheckOptions();
        configure?.Invoke(options);

        return builder.Add(new HealthCheckRegistration(
            checkName ?? $"valiblob-{providerName.ToLowerInvariant()}",
            sp =>
            {
                var factory = sp.GetRequiredService<IStorageFactory>();
                var provider = factory.Create(providerName);
                return new StorageProviderHealthCheck(provider, options);
            },
            failureStatus,
            tags));
    }
}
