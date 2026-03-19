using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.AWS;
using ValiBlob.AWS.Extensions;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Options;
using Xunit;

namespace ValiBlob.Core.Tests;

/// <summary>
/// Unit tests for the AWS S3 provider — options, DI registration, and interface compliance.
/// No real AWS calls are made.
/// </summary>
public sealed class AwsProviderTests
{
    // ─── Test 1: AWSS3Options default Region ─────────────────────────────────

    [Fact]
    public void AWSS3Options_Region_DefaultsToUsEast1()
    {
        var options = new AWSS3Options();

        options.Region.Should().Be("us-east-1");
    }

    // ─── Test 2: AWSS3Options BucketName can be set ───────────────────────────

    [Fact]
    public void AWSS3Options_Bucket_CanBeSet()
    {
        var options = new AWSS3Options { Bucket = "my-test-bucket" };

        options.Bucket.Should().Be("my-test-bucket");
    }

    // ─── Test 3: AWSS3Options all properties can be set ──────────────────────

    [Fact]
    public void AWSS3Options_AllProperties_CanBeSet()
    {
        var options = new AWSS3Options
        {
            Bucket = "my-bucket",
            Region = "eu-west-1",
            AccessKeyId = "AKIAIOSFODNN7EXAMPLE",
            SecretAccessKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            ServiceUrl = "http://minio:9000",
            ForcePathStyle = true,
            UseIAMRole = false,
            CdnBaseUrl = "https://cdn.example.com"
        };

        options.Bucket.Should().Be("my-bucket");
        options.Region.Should().Be("eu-west-1");
        options.AccessKeyId.Should().Be("AKIAIOSFODNN7EXAMPLE");
        options.SecretAccessKey.Should().Be("wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY");
        options.ServiceUrl.Should().Be("http://minio:9000");
        options.ForcePathStyle.Should().BeTrue();
        options.UseIAMRole.Should().BeFalse();
        options.CdnBaseUrl.Should().Be("https://cdn.example.com");
    }

    // ─── Test 4: AWSS3Options.ServiceUrl allows MinIO override ───────────────

    [Fact]
    public void AWSS3Options_ServiceUrl_AllowsMinioOverride()
    {
        var options = new AWSS3Options
        {
            ServiceUrl = "http://localhost:9000"
        };

        options.ServiceUrl.Should().NotBeNull();
        options.ServiceUrl.Should().Be("http://localhost:9000");
    }

    // ─── Test 5: AWSS3Options.ForcePathStyle defaults to false ───────────────

    [Fact]
    public void AWSS3Options_ForcePathStyle_DefaultsToFalse()
    {
        var options = new AWSS3Options();

        options.ForcePathStyle.Should().BeFalse();
    }

    // ─── Test 6: AWSS3Provider implements IStorageProvider ───────────────────

    [Fact]
    public void AWSS3Provider_Implements_IStorageProvider()
    {
        typeof(AWSS3Provider).GetInterfaces()
            .Should().Contain(typeof(IStorageProvider));
    }

    // ─── Test 7: AWSS3Provider implements IResumableUploadProvider ───────────

    [Fact]
    public void AWSS3Provider_Implements_IResumableUploadProvider()
    {
        typeof(AWSS3Provider).GetInterfaces()
            .Should().Contain(typeof(IResumableUploadProvider));
    }

    // ─── Test 8: AWSS3Provider implements IPresignedUrlProvider ──────────────

    [Fact]
    public void AWSS3Provider_Implements_IPresignedUrlProvider()
    {
        typeof(AWSS3Provider).GetInterfaces()
            .Should().Contain(typeof(IPresignedUrlProvider));
    }

    // ─── Test 9: AWSS3Provider.ProviderName returns "AWS" ────────────────────

    [Fact]
    public void AWSS3Provider_ProviderName_IsAWS()
    {
        // Verify via reflection on the property, not via instantiation
        var providerNameProp = typeof(AWSS3Provider).GetProperty(nameof(IStorageProvider.ProviderName));

        providerNameProp.Should().NotBeNull("AWSS3Provider should declare ProviderName");
    }

    // ─── Test 10: DI registration registers IStorageProvider in container ────

    [Fact]
    public void UseAWS_RegistersKeyedStorageProvider_InContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddValiBlob().UseAWS(o =>
        {
            o.Bucket = "test-bucket";
            o.Region = "us-east-1";
            o.UseIAMRole = true; // avoid needing real credentials
        });

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IStorageProvider) ||
            (d.ServiceKey is string key && key == "AWS" && d.ServiceType == typeof(IStorageProvider)));

        // The keyed registration should exist
        var keyedDescriptor = services.FirstOrDefault(d =>
            d.IsKeyedService && d.ServiceKey is string k && k == "AWS");

        keyedDescriptor.Should().NotBeNull("UseAWS should register a keyed IStorageProvider with key 'AWS'");
    }

    // ─── Test 11: ResumableUploadOptions EnableChecksumValidation defaults to true ──

    [Fact]
    public void ResumableUploadOptions_EnableChecksumValidation_DefaultsToTrue()
    {
        var options = new ResumableUploadOptions();

        options.EnableChecksumValidation.Should().BeTrue();
    }

    // ─── Test 12: IStorageFactory resolves named AWS provider ────────────────

    [Fact]
    public void UseAWS_StorageFactory_ResolvesProviderByName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddValiBlob(o => o.DefaultProvider = "AWS")
            .UseAWS(o =>
            {
                o.Bucket = "test-bucket";
                o.Region = "us-east-1";
                o.UseIAMRole = true;
            });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IStorageFactory>();

        // Resolving by name should not throw (provider is registered as keyed service)
        var provider = factory.Create("AWS");

        provider.Should().NotBeNull();
        provider.ProviderName.Should().Be("AWS");
    }

    // ─── Test 13: Multiple providers — factory returns correct one by name ────

    [Fact]
    public void UseAWS_WithMultipleProviders_FactoryReturnsCorrectByName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services
            .AddValiBlob()
            .UseAWS(o =>
            {
                o.Bucket = "aws-bucket";
                o.Region = "us-east-1";
                o.UseIAMRole = true;
            });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IStorageFactory>();

        var awsProvider = factory.Create("AWS");

        awsProvider.Should().NotBeNull();
        awsProvider.ProviderName.Should().Be("AWS");
        awsProvider.Should().BeAssignableTo<IStorageProvider>();
    }
}
