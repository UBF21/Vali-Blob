using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Options;

namespace ValiBlob.Supabase.Extensions;

/// <summary>
/// Dependency injection extensions for Supabase Storage provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Supabase Storage provider with the ValiStorageBuilder.
    /// </summary>
    /// <param name="builder">The ValiStorageBuilder instance.</param>
    /// <param name="configure">Optional configuration action for SupabaseStorageOptions.</param>
    /// <returns>The ValiStorageBuilder for chaining.</returns>
    public static ValiStorageBuilder UseSupabase(
        this ValiStorageBuilder builder,
        Action<SupabaseStorageOptions>? configure = null)
    {
        builder.Services.AddOptions<SupabaseStorageOptions>()
            .BindConfiguration(SupabaseStorageOptions.SectionName);

        if (configure is not null)
            builder.Services.Configure(configure);

        builder.Services.AddHttpClient<SupabaseStorageProvider>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<SupabaseStorageOptions>>().Value;
            client.BaseAddress = new Uri($"{opts.Url.TrimEnd('/')}/storage/v1/");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {opts.ApiKey}");
            client.DefaultRequestHeaders.Add("apikey", opts.ApiKey);
        });

        builder.Services.AddKeyedScoped<IStorageProvider, SupabaseStorageProvider>(nameof(StorageProviderType.Supabase));

        return builder;
    }
}
