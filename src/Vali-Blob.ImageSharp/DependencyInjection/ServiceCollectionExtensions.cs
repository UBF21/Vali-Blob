using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;

namespace ValiBlob.ImageSharp.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds image processing middleware (resize, reformat, thumbnail generation) to the ValiBlob pipeline.
    /// </summary>
    public static ValiStorageBuilder WithImageProcessing(
        this ValiStorageBuilder builder,
        Action<ImageProcessingOptions>? configure = null)
    {
        var options = new ImageProcessingOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);
        builder.Services.AddTransient<IStorageMiddleware>(sp =>
        {
            var factory = sp.GetRequiredService<IStorageFactory>();
            var provider = factory.Create();
            return new ImageProcessingMiddleware(options, provider);
        });

        return builder;
    }
}
