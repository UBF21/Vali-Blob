using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Core.Abstractions;

namespace ValiBlob.EFCore.DependencyInjection;

/// <summary>
/// Extension methods for registering the EF Core resumable session store.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="EfCoreResumableSessionStore"/> as the scoped <see cref="IResumableSessionStore"/>
    /// using a <typeparamref name="TContext"/> that is already registered in the container.
    /// </summary>
    /// <typeparam name="TContext">A subclass of <see cref="ValiResumableDbContext"/> registered elsewhere.</typeparam>
    public static IServiceCollection AddValiEfCoreSessionStore<TContext>(
        this IServiceCollection services)
        where TContext : ValiResumableDbContext
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        services.AddScoped<IResumableSessionStore, EfCoreResumableSessionStore>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="ValiResumableDbContext"/> and <see cref="EfCoreResumableSessionStore"/>
    /// as the scoped <see cref="IResumableSessionStore"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="optionsAction">Delegate to configure the <see cref="DbContextOptionsBuilder"/> (e.g. UseNpgsql, UseSqlServer).</param>
    public static IServiceCollection AddValiEfCoreSessionStore(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (optionsAction is null) throw new ArgumentNullException(nameof(optionsAction));

        services.AddDbContext<ValiResumableDbContext>(optionsAction);
        services.AddScoped<IResumableSessionStore, EfCoreResumableSessionStore>();
        return services;
    }
}
