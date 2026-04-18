using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Events;
using ValiBlob.Core.Options;
using ValiBlob.Core.Providers;
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

    private sealed class FakeEventHandler : IStorageEventHandler
    {
        public Task OnUploadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OnUploadFailedAsync(StorageEventContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OnDownloadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OnDeleteCompletedAsync(StorageEventContext context, CancellationToken cancellationToken) => Task.CompletedTask;
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
