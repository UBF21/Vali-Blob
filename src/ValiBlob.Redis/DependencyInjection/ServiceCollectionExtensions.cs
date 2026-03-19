using System;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using ValiBlob.Core.Abstractions;

namespace ValiBlob.Redis.DependencyInjection;

/// <summary>
/// Extension methods for registering the Redis-backed resumable session store.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RedisResumableSessionStore"/> as the <see cref="IResumableSessionStore"/> singleton,
    /// creating a new <see cref="IConnectionMultiplexer"/> from <paramref name="connectionString"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">Redis connection string (e.g. <c>localhost:6379</c>).</param>
    /// <param name="configure">Optional delegate to further configure <see cref="RedisSessionStoreOptions"/>.</param>
    public static IServiceCollection AddValiRedisSessionStore(
        this IServiceCollection services,
        string connectionString,
        Action<RedisSessionStoreOptions>? configure = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));

        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(connectionString));

        services.Configure<RedisSessionStoreOptions>(opts =>
        {
            opts.ConfigurationString = connectionString;
            configure?.Invoke(opts);
        });

        services.AddSingleton<IResumableSessionStore, RedisResumableSessionStore>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="RedisResumableSessionStore"/> using an existing <see cref="IConnectionMultiplexer"/>
    /// already registered in the container.
    /// </summary>
    public static IServiceCollection AddValiRedisSessionStore(
        this IServiceCollection services,
        Action<RedisSessionStoreOptions>? configure = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        if (configure is not null)
            services.Configure(configure);

        services.AddSingleton<IResumableSessionStore, RedisResumableSessionStore>();
        return services;
    }
}
