using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Cdn;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Events;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline.Middlewares;
using ValiBlob.Core.Providers;
using ValiBlob.Core.Quota;
using ValiBlob.Core.Resumable;
using ValiBlob.Core.Security;
using Xunit;

using Microsoft.Extensions.Configuration;

using Opts = Microsoft.Extensions.Options.Options;

namespace ValiBlob.Core.Tests;

// ─── StorageFactory ───────────────────────────────────────────────────────────

public sealed class StorageFactoryTests
{
    private static IStorageProvider MakeFakeProvider(string name = "InMemory")
    {
        var provider = Substitute.For<IStorageProvider>();
        provider.ProviderName.Returns(name);
        return provider;
    }

    private static IServiceProvider BuildSp(
        Action<IServiceCollection>? configure = null,
        StorageGlobalOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Opts.Create(options ?? new StorageGlobalOptions()));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Create_WithValidProviderKey_ReturnsProvider()
    {
        var fakeProvider = MakeFakeProvider();
        var sp = BuildSp(s => s.AddKeyedSingleton<IStorageProvider>("InMemory", fakeProvider));

        var factory = new StorageFactory(sp, Opts.Create(new StorageGlobalOptions()));

        var result = factory.Create("InMemory");

        result.Should().NotBeNull();
    }

    [Fact]
    public void Create_WithUnregisteredKey_ThrowsInvalidOperationException()
    {
        var sp = BuildSp();
        var factory = new StorageFactory(sp, Opts.Create(new StorageGlobalOptions()));

        var act = () => factory.Create("UnknownProvider");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*UnknownProvider*");
    }

    [Fact]
    public void Create_WithNullAndEmptyDefault_ThrowsInvalidOperationException()
    {
        var sp = BuildSp();
        var factory = new StorageFactory(sp, Opts.Create(new StorageGlobalOptions { DefaultProvider = "" }));

        var act = () => factory.Create(null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DefaultProvider*");
    }

    [Fact]
    public void Create_UsesDefaultProviderWhenNullArgument()
    {
        var fakeProvider = MakeFakeProvider("Local");
        var sp = BuildSp(s => s.AddKeyedSingleton<IStorageProvider>("Local", fakeProvider));
        var options = new StorageGlobalOptions { DefaultProvider = "Local" };

        var factory = new StorageFactory(sp, Opts.Create(options));

        var result = factory.Create(null);

        result.Should().NotBeNull();
    }

    [Fact]
    public void Create_WithTelemetryDecoratorEnabled_WrapsWithStorageTelemetryDecorator()
    {
        var fakeProvider = MakeFakeProvider();
        var sp = BuildSp(s => s.AddKeyedSingleton<IStorageProvider>("InMemory", fakeProvider));
        var options = new StorageGlobalOptions { ApplyTelemetryDecorator = true };

        var factory = new StorageFactory(sp, Opts.Create(options));
        var result = factory.Create("InMemory");

        result.Should().BeOfType<StorageTelemetryDecorator>();
    }

    [Fact]
    public void Create_WithEventDecoratorEnabled_WrapsWithStorageEventDecorator()
    {
        var fakeProvider = MakeFakeProvider();
        var fakeDispatcher = Substitute.For<IStorageEventDispatcher>();
        var sp = BuildSp(s =>
        {
            s.AddKeyedSingleton<IStorageProvider>("InMemory", fakeProvider);
            s.AddSingleton(fakeDispatcher);
        });
        var options = new StorageGlobalOptions { ApplyEventDecorator = true };

        var factory = new StorageFactory(sp, Opts.Create(options));
        var result = factory.Create("InMemory");

        result.Should().BeOfType<StorageEventDecorator>();
    }

    [Fact]
    public void Create_WithBothDecorators_AppliesEventDecoratorOutermost()
    {
        var fakeProvider = MakeFakeProvider();
        var fakeDispatcher = Substitute.For<IStorageEventDispatcher>();
        var sp = BuildSp(s =>
        {
            s.AddKeyedSingleton<IStorageProvider>("InMemory", fakeProvider);
            s.AddSingleton(fakeDispatcher);
        });
        var options = new StorageGlobalOptions { ApplyTelemetryDecorator = true, ApplyEventDecorator = true };

        var factory = new StorageFactory(sp, Opts.Create(options));
        var result = factory.Create("InMemory");

        // Event decorator applied last → outermost
        result.Should().BeOfType<StorageEventDecorator>();
    }

    [Fact]
    public void Create_WithNoDecorators_ReturnsRawProvider()
    {
        var fakeProvider = MakeFakeProvider();
        var sp = BuildSp(s => s.AddKeyedSingleton<IStorageProvider>("InMemory", fakeProvider));
        var options = new StorageGlobalOptions { ApplyTelemetryDecorator = false, ApplyEventDecorator = false };

        var factory = new StorageFactory(sp, Opts.Create(options));
        var result = factory.Create("InMemory");

        result.Should().BeSameAs(fakeProvider);
    }

    [Fact]
    public void Create_WithEventDecoratorButNoDispatcher_ReturnsRawProvider()
    {
        var fakeProvider = MakeFakeProvider();
        var sp = BuildSp(s => s.AddKeyedSingleton<IStorageProvider>("InMemory", fakeProvider));
        var options = new StorageGlobalOptions { ApplyEventDecorator = true };

        var factory = new StorageFactory(sp, Opts.Create(options));
        var result = factory.Create("InMemory");

        // When dispatcher is not registered, event decorator is NOT applied
        result.Should().BeSameAs(fakeProvider);
    }

    [Fact]
    public void GetAll_WithRegisteredProviders_ReturnsThem()
    {
        var localProvider = MakeFakeProvider("Local");
        var sp = BuildSp(s => s.AddKeyedSingleton<IStorageProvider>("Local", localProvider));

        var factory = new StorageFactory(sp, Opts.Create(new StorageGlobalOptions()));
        var providers = factory.GetAll().ToList();

        providers.Should().HaveCountGreaterOrEqualTo(1);
    }
}

// ─── ValiStorageBuilder ───────────────────────────────────────────────────────

public sealed class ValiStorageBuilderTests
{
    private static (ValiStorageBuilder Builder, IServiceCollection Services) CreateBuilder()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var builder = services.AddValiBlob();
        return (builder, services);
    }

    private static StorageGlobalOptions ResolveOptions(IServiceCollection services)
    {
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptions<StorageGlobalOptions>>().Value;
    }

    [Fact]
    public void WithTelemetry_SetsApplyTelemetryDecoratorTrue()
    {
        var (builder, services) = CreateBuilder();

        builder.WithTelemetry();

        var opts = ResolveOptions(services);
        opts.ApplyTelemetryDecorator.Should().BeTrue();
    }

    [Fact]
    public void WithTelemetry_SetsEnableTelemetryTrue()
    {
        var (builder, services) = CreateBuilder();

        builder.WithTelemetry();

        var opts = ResolveOptions(services);
        opts.EnableTelemetry.Should().BeTrue();
    }

    [Fact]
    public void WithEventDispatching_SetsApplyEventDecoratorTrue()
    {
        var (builder, services) = CreateBuilder();

        builder.WithEventDispatching();

        var opts = ResolveOptions(services);
        opts.ApplyEventDecorator.Should().BeTrue();
    }

    [Fact]
    public void WithDefaultProvider_ByString_SetsDefaultProvider()
    {
        var (builder, services) = CreateBuilder();

        builder.WithDefaultProvider("AWS");

        var opts = ResolveOptions(services);
        opts.DefaultProvider.Should().Be("AWS");
    }

    [Fact]
    public void WithDefaultProvider_ByEnum_SetsDefaultProviderString()
    {
        var (builder, services) = CreateBuilder();

        builder.WithDefaultProvider(StorageProviderType.Azure);

        var opts = ResolveOptions(services);
        opts.DefaultProvider.Should().Be("Azure");
    }

    [Fact]
    public void WithDefaultProvider_WithNoneEnum_SetsEmptyDefault()
    {
        var (builder, services) = CreateBuilder();

        builder.WithDefaultProvider(StorageProviderType.None);

        var opts = ResolveOptions(services);
        opts.DefaultProvider.Should().BeEmpty();
    }

    [Fact]
    public void WithDefaultProvider_WithCustomEnum_ThrowsArgumentException()
    {
        var (builder, _) = CreateBuilder();

        var act = () => builder.WithDefaultProvider(StorageProviderType.Custom);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithEventHandler_RegistersHandlerAsSingleton()
    {
        var (builder, services) = CreateBuilder();

        builder.WithEventHandler<FakeEventHandler>();

        var sp = services.BuildServiceProvider();
        var handlers = sp.GetServices<IStorageEventHandler>();
        handlers.Should().ContainItemsAssignableTo<FakeEventHandler>();
    }

    [Fact]
    public void FluentChaining_ReturnsSameBuilderInstance()
    {
        var (builder, _) = CreateBuilder();

        var result = builder
            .WithTelemetry()
            .WithEventDispatching()
            .WithDefaultProvider("InMemory");

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithResiliencePolicies_ConfiguresRetryCount()
    {
        var (builder, services) = CreateBuilder();

        builder.WithResiliencePolicies(o => o.RetryCount = 7);

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<ResilienceOptions>>().Value;
        opts.RetryCount.Should().Be(7);
    }

    [Fact]
    public void WithResiliencePolicies_ReturnsSameBuilder()
    {
        var (builder, _) = CreateBuilder();
        var result = builder.WithResiliencePolicies(o => o.RetryCount = 1);
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithPipeline_InvokesConfigureCallback()
    {
        var (builder, _) = CreateBuilder();
        var called = false;

        builder.WithPipeline(_ => called = true);

        called.Should().BeTrue();
    }

    [Fact]
    public void WithPipeline_ReturnsSameBuilder()
    {
        var (builder, _) = CreateBuilder();
        var result = builder.WithPipeline(_ => { });
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithResumableUploads_ConfiguresSessionExpiration()
    {
        var (builder, services) = CreateBuilder();

        builder.WithResumableUploads(o => o.SessionExpiration = TimeSpan.FromHours(48));

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<ResumableUploadOptions>>().Value;
        opts.SessionExpiration.Should().Be(TimeSpan.FromHours(48));
    }

    [Fact]
    public void WithResumableUploads_ReturnsSameBuilder()
    {
        var (builder, _) = CreateBuilder();
        var result = builder.WithResumableUploads(_ => { });
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void UseResumableSessionStore_ReplacesDefaultWithCustomStore()
    {
        var (builder, services) = CreateBuilder();

        builder.UseResumableSessionStore<FakeResumableSessionStore>();

        var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IResumableSessionStore>();
        store.Should().BeOfType<FakeResumableSessionStore>();
    }

    [Fact]
    public void UseResumableSessionStore_ReturnsSameBuilder()
    {
        var (builder, _) = CreateBuilder();
        var result = builder.UseResumableSessionStore<FakeResumableSessionStore>();
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithCdn_ConfiguresBaseUrl()
    {
        var (builder, services) = CreateBuilder();

        builder.WithCdn(o => o.BaseUrl = "https://cdn.example.com");

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<CdnOptions>>().Value;
        opts.BaseUrl.Should().Be("https://cdn.example.com");
    }

    [Fact]
    public void WithCdn_RegistersICdnProvider()
    {
        var (builder, services) = CreateBuilder();

        builder.WithCdn(o => o.BaseUrl = "https://cdn.example.com");

        var sp = services.BuildServiceProvider();
        sp.GetService<ICdnProvider>().Should().NotBeNull().And.BeOfType<PrefixCdnProvider>();
    }

    [Fact]
    public void WithCdn_ReturnsSameBuilder()
    {
        var (builder, _) = CreateBuilder();
        var result = builder.WithCdn(_ => { });
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithContentTypeDetection_RegistersMiddleware()
    {
        var (builder, services) = CreateBuilder();

        builder.WithContentTypeDetection();

        services.Any(d => d.ImplementationType == typeof(ContentTypeDetectionMiddleware))
            .Should().BeTrue();
    }

    [Fact]
    public void WithContentTypeDetection_ReturnsSameBuilder()
    {
        var (builder, _) = CreateBuilder();
        var result = builder.WithContentTypeDetection();
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithDeduplication_RegistersMiddleware()
    {
        var (builder, services) = CreateBuilder();

        builder.WithDeduplication();

        services.Any(d => d.ImplementationType == typeof(DeduplicationMiddleware))
            .Should().BeTrue();
    }

    [Fact]
    public void WithDeduplication_ReturnsSameBuilder()
    {
        var (builder, _) = CreateBuilder();
        var result = builder.WithDeduplication();
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithVirusScanning_WithoutScanner_RegistersNoOpVirusScanner()
    {
        var (builder, services) = CreateBuilder();

        builder.WithVirusScanning();

        var sp = services.BuildServiceProvider();
        sp.GetService<IVirusScanner>().Should().NotBeNull().And.BeOfType<NoOpVirusScanner>();
    }

    [Fact]
    public void WithVirusScanning_WithCustomScanner_RegistersProvidedInstance()
    {
        var (builder, services) = CreateBuilder();
        var customScanner = Substitute.For<IVirusScanner>();

        builder.WithVirusScanning(customScanner);

        var sp = services.BuildServiceProvider();
        sp.GetService<IVirusScanner>().Should().BeSameAs(customScanner);
    }

    [Fact]
    public void WithVirusScanning_ReturnsSameBuilder()
    {
        var (builder, _) = CreateBuilder();
        var result = builder.WithVirusScanning();
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithStorageQuota_RegistersQuotaMiddleware()
    {
        var (builder, services) = CreateBuilder();

        builder.WithStorageQuota(o => o.DefaultLimitBytes = 100 * 1024 * 1024);

        services.Any(d => d.ImplementationType == typeof(QuotaMiddleware))
            .Should().BeTrue();
    }

    [Fact]
    public void WithStorageQuota_ReturnsSameBuilder()
    {
        var (builder, _) = CreateBuilder();
        var result = builder.WithStorageQuota(o => o.DefaultLimitBytes = 1024);
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithRateLimit_RegistersRateLimitMiddleware()
    {
        var (builder, services) = CreateBuilder();

        builder.WithRateLimit(o => { o.MaxRequestsPerWindow = 100; o.Window = TimeSpan.FromMinutes(1); });

        services.Any(d => d.ImplementationInstance is RateLimitMiddleware)
            .Should().BeTrue();
    }

    [Fact]
    public void WithRateLimit_ReturnsSameBuilder()
    {
        var (builder, _) = CreateBuilder();
        var result = builder.WithRateLimit(o => { o.MaxRequestsPerWindow = 10; o.Window = TimeSpan.FromSeconds(1); });
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithConflictResolution_RegistersMiddleware()
    {
        var (builder, services) = CreateBuilder();

        builder.WithConflictResolution();

        services.Any(d => d.ImplementationType == typeof(ConflictResolutionMiddleware))
            .Should().BeTrue();
    }

    [Fact]
    public void WithConflictResolution_ReturnsSameBuilder()
    {
        var (builder, _) = CreateBuilder();
        var result = builder.WithConflictResolution();
        result.Should().BeSameAs(builder);
    }

    private sealed class FakeEventHandler : IStorageEventHandler
    {
        public Task OnUploadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OnUploadFailedAsync(StorageEventContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OnDownloadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OnDeleteCompletedAsync(StorageEventContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeResumableSessionStore : IResumableSessionStore
    {
        public Task SaveAsync(ValiBlob.Core.Models.ResumableUploadSession session, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ValiBlob.Core.Models.ResumableUploadSession?> GetAsync(string uploadId, CancellationToken cancellationToken = default) => Task.FromResult<ValiBlob.Core.Models.ResumableUploadSession?>(null);
        public Task UpdateAsync(ValiBlob.Core.Models.ResumableUploadSession session, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string uploadId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

// ─── ServiceCollectionExtensions ─────────────────────────────────────────────

public sealed class ServiceCollectionExtensionsTests
{
    private static IServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        return services;
    }

    [Fact]
    public void AddValiBlob_RegistersIStorageFactory()
    {
        var services = CreateServices();
        services.AddValiBlob();
        var sp = services.BuildServiceProvider();
        sp.GetService<IStorageFactory>().Should().NotBeNull();
    }

    [Fact]
    public void AddValiBlob_RegistersStorageEventDispatcher()
    {
        var services = CreateServices();
        services.AddValiBlob();
        var sp = services.BuildServiceProvider();
        sp.GetService<IStorageEventDispatcher>().Should().NotBeNull();
    }

    [Fact]
    public void AddValiBlob_RegistersStoragePipelineBuilder()
    {
        var services = CreateServices();
        services.AddValiBlob();
        var sp = services.BuildServiceProvider();
        var pipeline = sp.GetService<Pipeline.StoragePipelineBuilder>();
        pipeline.Should().NotBeNull();
    }

    [Fact]
    public void AddValiBlob_RegistersStorageGlobalOptions()
    {
        var services = CreateServices();
        services.AddValiBlob();
        var sp = services.BuildServiceProvider();
        sp.GetService<IOptions<StorageGlobalOptions>>().Should().NotBeNull();
    }

    [Fact]
    public void AddValiBlob_WithConfigure_AppliesOptions()
    {
        var services = CreateServices();
        services.AddValiBlob(o => o.EnableLogging = false);
        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<StorageGlobalOptions>>().Value;
        opts.EnableLogging.Should().BeFalse();
    }

    [Fact]
    public void AddValiBlob_ReturnsValiStorageBuilderInstance()
    {
        var services = CreateServices();
        var result = services.AddValiBlob();
        result.Should().NotBeNull().And.BeOfType<ValiStorageBuilder>();
    }

    [Fact]
    public void AddValiBlob_CalledTwice_DoesNotDuplicateRegistrations()
    {
        var services = CreateServices();
        services.AddValiBlob();
        services.AddValiBlob();
        var sp = services.BuildServiceProvider();
        var factories = sp.GetServices<IStorageFactory>();
        factories.Should().HaveCount(1);
    }
}
