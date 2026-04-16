using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Supabase;
using ValiBlob.Supabase.Extensions;
using Xunit;

namespace ValiBlob.Core.Tests;

/// <summary>
/// Unit tests for the Supabase Storage provider — options, DI registration, and interface compliance.
/// No real Supabase calls are made.
/// </summary>
public sealed class SupabaseProviderTests
{
    // ─── Test 1: SupabaseStorageOptions default values ────────────────────────

    [Fact]
    public void SupabaseStorageOptions_Defaults_AreCorrect()
    {
        var options = new SupabaseStorageOptions();

        options.Url.Should().Be(string.Empty);
        options.ApiKey.Should().Be(string.Empty);
        options.Bucket.Should().Be(string.Empty);
        options.CdnBaseUrl.Should().BeNull();
    }

    // ─── Test 2: SupabaseStorageOptions all properties can be set ────────────

    [Fact]
    public void SupabaseStorageOptions_AllProperties_CanBeSet()
    {
        var options = new SupabaseStorageOptions
        {
            Url = "https://xyzcompany.supabase.co",
            ApiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.test",
            Bucket = "my-bucket",
            CdnBaseUrl = "https://cdn.example.com"
        };

        options.Url.Should().Be("https://xyzcompany.supabase.co");
        options.ApiKey.Should().Be("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.test");
        options.Bucket.Should().Be("my-bucket");
        options.CdnBaseUrl.Should().Be("https://cdn.example.com");
    }

    // ─── Test 3: SupabaseStorageProvider implements IStorageProvider ──────────

    [Fact]
    public void SupabaseStorageProvider_Implements_IStorageProvider()
    {
        typeof(SupabaseStorageProvider).GetInterfaces()
            .Should().Contain(typeof(IStorageProvider));
    }

    // ─── Test 4: SupabaseStorageProvider implements IResumableUploadProvider ──

    [Fact]
    public void SupabaseStorageProvider_Implements_IResumableUploadProvider()
    {
        typeof(SupabaseStorageProvider).GetInterfaces()
            .Should().Contain(typeof(IResumableUploadProvider));
    }

    // ─── Test 5: SupabaseStorageProvider implements IPresignedUrlProvider ──────

    [Fact]
    public void SupabaseStorageProvider_Implements_IPresignedUrlProvider()
    {
        typeof(SupabaseStorageProvider).GetInterfaces()
            .Should().Contain(typeof(IPresignedUrlProvider));
    }

    // ─── Test 6: SupabaseStorageProvider.ProviderName property exists ─────────

    [Fact]
    public void SupabaseStorageProvider_ProviderName_PropertyExists()
    {
        var prop = typeof(SupabaseStorageProvider).GetProperty(nameof(IStorageProvider.ProviderName));

        prop.Should().NotBeNull("SupabaseStorageProvider should declare ProviderName");
    }

    // ─── Test 7: DI registration via UseSupabase registers provider ───────────

    [Fact]
    public void UseSupabase_RegistersKeyedStorageProvider_InContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddValiBlob().UseSupabase(o =>
        {
            o.Url = "https://xyzcompany.supabase.co";
            o.ApiKey = "test-api-key";
            o.Bucket = "test-bucket";
        });

        var keyedDescriptor = services.FirstOrDefault(d =>
            d.IsKeyedService && d.ServiceKey is string k && k == "Supabase");

        keyedDescriptor.Should().NotBeNull("UseSupabase should register a keyed IStorageProvider with key 'Supabase'");
    }

    // ─── Test 8: Container resolves IStorageProvider after UseSupabase ─────────

    [Fact]
    public void UseSupabase_Container_ResolvesIStorageProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddValiBlob(o => o.DefaultProvider = "Supabase").UseSupabase(o =>
        {
            o.Url = "https://xyzcompany.supabase.co";
            o.ApiKey = "test-api-key";
            o.Bucket = "test-bucket";
        });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IStorageFactory>();
        var provider = factory.Create("Supabase");

        provider.Should().NotBeNull();
        provider.Should().BeAssignableTo<IStorageProvider>();
    }

    // ─── Test 9: SupabaseStorageOptions.Url with trailing slash is valid ───────

    [Fact]
    public void SupabaseStorageOptions_Url_WithTrailingSlash_IsValid()
    {
        var options = new SupabaseStorageOptions
        {
            Url = "https://xyzcompany.supabase.co/"
        };

        options.Url.Should().EndWith("/");
        options.Url.TrimEnd('/').Should().Be("https://xyzcompany.supabase.co");
    }

    // ─── Test 10: SupabaseStorageOptions.ApiKey can be set ────────────────────

    [Fact]
    public void SupabaseStorageOptions_ApiKey_CanBeSet()
    {
        var apiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.service-role-key";
        var options = new SupabaseStorageOptions { ApiKey = apiKey };

        options.ApiKey.Should().Be(apiKey);
        options.ApiKey.Should().NotBeNullOrWhiteSpace();
    }

    // ─── Test 11: Named provider accessible via IStorageFactory ───────────────

    [Fact]
    public void UseSupabase_StorageFactory_ResolvesProviderByName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddValiBlob().UseSupabase(o =>
        {
            o.Url = "https://xyzcompany.supabase.co";
            o.ApiKey = "test-api-key";
            o.Bucket = "test";
        });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IStorageFactory>();
        var provider = factory.Create("Supabase");

        provider.Should().NotBeNull();
        provider.ProviderName.Should().Be("Supabase");
    }

    // ─── Test 12: SupabaseStorageProvider actual interface list verification ───

    [Fact]
    public void SupabaseStorageProvider_InterfaceList_ContainsExpectedInterfaces()
    {
        var interfaces = typeof(SupabaseStorageProvider).GetInterfaces();

        interfaces.Should().Contain(typeof(IStorageProvider));
        interfaces.Should().Contain(typeof(IResumableUploadProvider));
        interfaces.Should().Contain(typeof(IPresignedUrlProvider));
    }
}
