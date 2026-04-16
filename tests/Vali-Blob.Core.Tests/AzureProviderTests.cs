using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Azure;
using ValiBlob.Azure.Extensions;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using Xunit;

namespace ValiBlob.Core.Tests;

/// <summary>
/// Unit tests for the Azure Blob provider — options, DI registration, and interface compliance.
/// No real Azure calls are made.
/// </summary>
public sealed class AzureProviderTests
{
    // ─── Test 1: AzureBlobOptions default values ─────────────────────────────

    [Fact]
    public void AzureBlobOptions_Defaults_AreCorrect()
    {
        var options = new AzureBlobOptions();

        options.Container.Should().Be(string.Empty);
        options.ConnectionString.Should().BeNull();
        options.AccountName.Should().BeNull();
        options.AccountKey.Should().BeNull();
        options.CdnBaseUrl.Should().BeNull();
        options.CreateContainerIfNotExists.Should().BeTrue();
    }

    // ─── Test 2: AzureBlobOptions all properties can be set ──────────────────

    [Fact]
    public void AzureBlobOptions_AllProperties_CanBeSet()
    {
        var options = new AzureBlobOptions
        {
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=abc123==",
            Container = "my-container",
            AccountName = "myaccount",
            AccountKey = "abc123==",
            CdnBaseUrl = "https://cdn.example.com",
            CreateContainerIfNotExists = false
        };

        options.ConnectionString.Should().Contain("AccountName=myaccount");
        options.Container.Should().Be("my-container");
        options.AccountName.Should().Be("myaccount");
        options.AccountKey.Should().Be("abc123==");
        options.CdnBaseUrl.Should().Be("https://cdn.example.com");
        options.CreateContainerIfNotExists.Should().BeFalse();
    }

    // ─── Test 3: AzureBlobOptions.CreateContainerIfNotExists defaults to true ─

    [Fact]
    public void AzureBlobOptions_CreateContainerIfNotExists_DefaultsToTrue()
    {
        var options = new AzureBlobOptions();

        options.CreateContainerIfNotExists.Should().BeTrue();
    }

    // ─── Test 4: AzureBlobProvider implements IStorageProvider ───────────────

    [Fact]
    public void AzureBlobProvider_Implements_IStorageProvider()
    {
        typeof(AzureBlobProvider).GetInterfaces()
            .Should().Contain(typeof(IStorageProvider));
    }

    // ─── Test 5: AzureBlobProvider implements IResumableUploadProvider ────────

    [Fact]
    public void AzureBlobProvider_Implements_IResumableUploadProvider()
    {
        typeof(AzureBlobProvider).GetInterfaces()
            .Should().Contain(typeof(IResumableUploadProvider));
    }

    // ─── Test 6: AzureBlobProvider implements IPresignedUrlProvider ──────────

    [Fact]
    public void AzureBlobProvider_Implements_IPresignedUrlProvider()
    {
        typeof(AzureBlobProvider).GetInterfaces()
            .Should().Contain(typeof(IPresignedUrlProvider));
    }

    // ─── Test 7: AzureBlobProvider.ProviderName property exists ──────────────

    [Fact]
    public void AzureBlobProvider_ProviderName_PropertyExists()
    {
        var providerNameProp = typeof(AzureBlobProvider).GetProperty(nameof(IStorageProvider.ProviderName));

        providerNameProp.Should().NotBeNull("AzureBlobProvider should declare ProviderName");
    }

    // ─── Test 8: DI registration via UseAzure registers provider ─────────────

    [Fact]
    public void UseAzure_RegistersKeyedStorageProvider_InContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddValiBlob().UseAzure(o =>
        {
            o.ConnectionString = "UseDevelopmentStorage=true";
            o.Container = "test-container";
        });

        var keyedDescriptor = services.FirstOrDefault(d =>
            d.IsKeyedService && d.ServiceKey is string k && k == "Azure");

        keyedDescriptor.Should().NotBeNull("UseAzure should register a keyed IStorageProvider with key 'Azure'");
    }

    // ─── Test 9: Container resolves IStorageProvider after UseAzure ──────────

    [Fact]
    public void UseAzure_Container_ResolvesIStorageProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddValiBlob(o => o.DefaultProvider = "Azure").UseAzure(o =>
        {
            o.ConnectionString = "UseDevelopmentStorage=true";
            o.Container = "test-container";
        });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IStorageFactory>();
        var provider = factory.Create("Azure");

        provider.Should().NotBeNull();
        provider.Should().BeAssignableTo<IStorageProvider>();
    }

    // ─── Test 10: AzureBlobOptions with ConnectionString is valid ─────────────

    [Fact]
    public void AzureBlobOptions_WithConnectionString_IsValidConfigObject()
    {
        var options = new AzureBlobOptions
        {
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;",
            Container = "my-container"
        };

        options.ConnectionString.Should().NotBeNullOrWhiteSpace();
        options.Container.Should().Be("my-container");
    }

    // ─── Test 11: AzureBlobOptions with AccountName+AccountKey is valid ────────

    [Fact]
    public void AzureBlobOptions_WithAccountNameAndKey_IsValidConfigObject()
    {
        var options = new AzureBlobOptions
        {
            AccountName = "mystorageaccount",
            AccountKey = "dGVzdGtleQ==",
            Container = "data"
        };

        options.AccountName.Should().Be("mystorageaccount");
        options.AccountKey.Should().Be("dGVzdGtleQ==");
        options.Container.Should().Be("data");
        options.ConnectionString.Should().BeNull();
    }

    // ─── Test 12: Named provider accessible via IStorageFactory ───────────────

    [Fact]
    public void UseAzure_StorageFactory_ResolvesProviderByName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddValiBlob().UseAzure(o =>
        {
            o.ConnectionString = "UseDevelopmentStorage=true";
            o.Container = "test";
        });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IStorageFactory>();
        var provider = factory.Create("Azure");

        provider.Should().NotBeNull();
        provider.ProviderName.Should().Be("Azure");
    }
}
