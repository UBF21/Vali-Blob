using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;

namespace ValiBlob.Supabase.Extensions;

public static class ServiceCollectionExtensions
{
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

        builder.Services.AddKeyedScoped<IStorageProvider, SupabaseStorageProvider>("Supabase");

        return builder;
    }
}
