using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Cdn;
using ValiBlob.Core.Events;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Pipeline.Middlewares;
using ValiBlob.Core.Quota;
using ValiBlob.Core.Security;

namespace ValiBlob.Core.DependencyInjection;

public sealed class ValiStorageBuilder
{
    public IServiceCollection Services { get; }

    public ValiStorageBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public ValiStorageBuilder WithResiliencePolicies(Action<ResilienceOptions> configure)
    {
        Services.Configure(configure);
        return this;
    }

    public ValiStorageBuilder WithPipeline(Action<PipelineConfigurator> configure)
    {
        var configurator = new PipelineConfigurator(Services);
        configure(configurator);
        return this;
    }

    public ValiStorageBuilder WithDefaultProvider(string providerName)
    {
        Services.Configure<StorageGlobalOptions>(o => o.DefaultProvider = providerName);
        return this;
    }

    public ValiStorageBuilder WithEventHandlers(Action<IServiceCollection> configure)
    {
        configure(Services);
        return this;
    }

    public ValiStorageBuilder WithEventHandler<THandler>() where THandler : class, IStorageEventHandler
    {
        Services.AddSingleton<IStorageEventHandler, THandler>();
        return this;
    }

    /// <summary>
    /// Configures global options for resumable (multi-chunk) uploads.
    /// </summary>
    public ValiStorageBuilder WithResumableUploads(Action<ResumableUploadOptions> configure)
    {
        Services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Replaces the default in-memory resumable session store with a custom implementation.
    /// Use this for distributed or persistent session storage (Redis, SQL, etc.).
    /// </summary>
    public ValiStorageBuilder UseResumableSessionStore<TStore>()
        where TStore : class, IResumableSessionStore
    {
        Services.RemoveAll<IResumableSessionStore>();
        Services.AddSingleton<IResumableSessionStore, TStore>();
        return this;
    }

    /// <summary>
    /// Registers a CDN URL provider using the simple prefix-based implementation.
    /// </summary>
    public ValiStorageBuilder WithCdn(Action<CdnOptions> configure)
    {
        Services.Configure(configure);
        Services.TryAddSingleton<ICdnProvider, PrefixCdnProvider>();
        return this;
    }

    /// <summary>
    /// Adds the Content-Type detection middleware (magic bytes).
    /// When OverrideExisting is false (default), only fills in ContentType when it is null.
    /// </summary>
    public ValiStorageBuilder WithContentTypeDetection(Action<ContentTypeDetectionOptions>? configure = null)
    {
        var opts = new ContentTypeDetectionOptions();
        configure?.Invoke(opts);
        Services.AddSingleton(opts);
        Services.TryAddEnumerable(ServiceDescriptor.Transient<IStorageMiddleware, ContentTypeDetectionMiddleware>());
        return this;
    }

    /// <summary>
    /// Adds SHA-256 file deduplication middleware.
    /// Files that share a content hash with an already-uploaded file are short-circuited.
    /// </summary>
    public ValiStorageBuilder WithDeduplication(Action<DeduplicationOptions>? configure = null)
    {
        var opts = new DeduplicationOptions();
        configure?.Invoke(opts);
        Services.AddSingleton(opts);
        Services.TryAddEnumerable(ServiceDescriptor.Transient<IStorageMiddleware, DeduplicationMiddleware>());
        return this;
    }

    /// <summary>
    /// Adds virus-scanning middleware. If no scanner is provided, the no-op scanner is used.
    /// Register a real <see cref="IVirusScanner"/> implementation for production use.
    /// </summary>
    public ValiStorageBuilder WithVirusScanning(IVirusScanner? scanner = null)
    {
        if (scanner is not null)
            Services.TryAddSingleton<IVirusScanner>(scanner);
        else
            Services.TryAddSingleton<IVirusScanner, NoOpVirusScanner>();

        Services.TryAddEnumerable(ServiceDescriptor.Transient<IStorageMiddleware, VirusScanMiddleware>());
        return this;
    }

    /// <summary>
    /// Adds storage quota enforcement middleware backed by an in-memory usage tracker.
    /// </summary>
    public ValiStorageBuilder WithStorageQuota(Action<QuotaOptions> configure)
    {
        var opts = new QuotaOptions();
        configure(opts);
        Services.AddSingleton(opts);
        Services.TryAddSingleton<IStorageQuotaService>(sp =>
            new InMemoryStorageQuotaService(sp.GetRequiredService<QuotaOptions>()));
        Services.TryAddEnumerable(ServiceDescriptor.Transient<IStorageMiddleware, QuotaMiddleware>());
        return this;
    }

    /// <summary>
    /// Adds conflict resolution middleware that checks whether a file already exists
    /// and acts according to <see cref="Models.ConflictResolution"/> set on each upload request.
    /// </summary>
    public ValiStorageBuilder WithConflictResolution()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Transient<IStorageMiddleware, ConflictResolutionMiddleware>());
        return this;
    }
}
