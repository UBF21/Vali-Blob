using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline.Middlewares;
using Xunit;

namespace ValiBlob.Core.Tests;

// ─── UploadOptions ────────────────────────────────────────────────────────────

public sealed class UploadOptionsTests
{
    [Fact]
    public void UseMultipart_DefaultValue_IsFalse()
    {
        var options = new UploadOptions();
        options.UseMultipart.Should().BeFalse();
    }

    [Fact]
    public void ChunkSizeMb_DefaultValue_IsEight()
    {
        var options = new UploadOptions();
        options.ChunkSizeMb.Should().Be(8);
    }

    [Fact]
    public void Overwrite_DefaultValue_IsTrue()
    {
        var options = new UploadOptions();
        options.Overwrite.Should().BeTrue();
    }

    [Fact]
    public void Encryption_DefaultValue_IsNone()
    {
        var options = new UploadOptions();
        options.Encryption.Should().Be(StorageEncryptionMode.None);
    }

    [Fact]
    public void Construction_WithAllProperties_StoresCorrectly()
    {
        var options = new UploadOptions
        {
            UseMultipart = true,
            ChunkSizeMb = 16,
            Overwrite = false,
            Encryption = StorageEncryptionMode.ClientSide
        };

        options.UseMultipart.Should().BeTrue();
        options.ChunkSizeMb.Should().Be(16);
        options.Overwrite.Should().BeFalse();
        options.Encryption.Should().Be(StorageEncryptionMode.ClientSide);
    }

    [Fact]
    public void Encryption_ProviderManaged_CanBeSet()
    {
        var options = new UploadOptions { Encryption = StorageEncryptionMode.ProviderManaged };
        options.Encryption.Should().Be(StorageEncryptionMode.ProviderManaged);
    }
}

// ─── UploadRequest — ConflictResolution and BucketOverride defaults ───────────

public sealed class UploadRequestDefaultsTests
{
    private static UploadRequest BaseRequest() => new()
    {
        Path = StoragePath.From("uploads/file.txt"),
        Content = Stream.Null
    };

    [Fact]
    public void ConflictResolution_DefaultValue_IsOverwrite()
    {
        var request = BaseRequest();
        request.ConflictResolution.Should().Be(ConflictResolution.Overwrite);
    }

    [Fact]
    public void BucketOverride_DefaultValue_IsNull()
    {
        var request = BaseRequest();
        request.BucketOverride.Should().BeNull();
    }

    [Fact]
    public void Options_DefaultValue_IsNull()
    {
        var request = BaseRequest();
        request.Options.Should().BeNull();
    }

    [Fact]
    public void ContentLength_DefaultValue_IsNull()
    {
        var request = BaseRequest();
        request.ContentLength.Should().BeNull();
    }

    [Fact]
    public void Metadata_DefaultValue_IsNull()
    {
        var request = BaseRequest();
        request.Metadata.Should().BeNull();
    }

    [Fact]
    public void WithContent_PreservesConflictResolution()
    {
        var original = new UploadRequest
        {
            Path = StoragePath.From("file.txt"),
            Content = Stream.Null,
            ConflictResolution = ConflictResolution.Fail
        };

        var updated = original.WithContent(new MemoryStream());

        updated.ConflictResolution.Should().Be(ConflictResolution.Fail);
    }

    [Fact]
    public void WithContent_PreservesBucketOverride()
    {
        var original = new UploadRequest
        {
            Path = StoragePath.From("file.txt"),
            Content = Stream.Null,
            BucketOverride = "tenant-bucket"
        };

        var updated = original.WithContent(new MemoryStream());

        updated.BucketOverride.Should().Be("tenant-bucket");
    }

    [Fact]
    public void WithPath_PreservesBucketOverride()
    {
        var original = new UploadRequest
        {
            Path = StoragePath.From("file.txt"),
            Content = Stream.Null,
            BucketOverride = "custom-bucket"
        };

        var updated = original.WithPath(StoragePath.From("other.txt"));

        updated.BucketOverride.Should().Be("custom-bucket");
    }

    [Fact]
    public void WithPath_PreservesConflictResolution()
    {
        var original = new UploadRequest
        {
            Path = StoragePath.From("file.txt"),
            Content = Stream.Null,
            ConflictResolution = ConflictResolution.Rename
        };

        var updated = original.WithPath(StoragePath.From("other.txt"));

        updated.ConflictResolution.Should().Be(ConflictResolution.Rename);
    }

    [Fact]
    public void WithMetadata_PreservesConflictResolution()
    {
        var original = new UploadRequest
        {
            Path = StoragePath.From("file.txt"),
            Content = Stream.Null,
            ConflictResolution = ConflictResolution.Fail
        };

        var updated = original.WithMetadata(new Dictionary<string, string>());

        updated.ConflictResolution.Should().Be(ConflictResolution.Fail);
    }

    [Fact]
    public void WithContentType_PreservesConflictResolution()
    {
        var original = new UploadRequest
        {
            Path = StoragePath.From("file.txt"),
            Content = Stream.Null,
            ConflictResolution = ConflictResolution.Rename
        };

        var updated = original.WithContentType("image/png");

        updated.ConflictResolution.Should().Be(ConflictResolution.Rename);
    }
}

// ─── StorageResult — ToString, explicit error codes, Exception ────────────────

public sealed class StorageResultExtendedTests
{
    [Fact]
    public void NonGeneric_Success_ToString_ReturnsSuccessLabel()
    {
        var result = StorageResult.Success();
        result.ToString().Should().Be("Success");
    }

    [Fact]
    public void NonGeneric_Failure_ToString_ContainsErrorCodeAndMessage()
    {
        var result = StorageResult.Failure("file missing", StorageErrorCode.FileNotFound);
        result.ToString().Should().Contain("FileNotFound").And.Contain("file missing");
    }

    [Fact]
    public void NonGeneric_Failure_WithExplicitErrorCode_StoresCode()
    {
        var result = StorageResult.Failure("denied", StorageErrorCode.AccessDenied);
        result.ErrorCode.Should().Be(StorageErrorCode.AccessDenied);
    }

    [Fact]
    public void NonGeneric_Failure_WithException_StoresException()
    {
        var ex = new IOException("disk full");
        var result = StorageResult.Failure("io error", ex: ex);
        result.Exception.Should().BeSameAs(ex);
    }

    [Fact]
    public void NonGeneric_Success_ErrorCode_IsNone()
    {
        var result = StorageResult.Success();
        result.ErrorCode.Should().Be(StorageErrorCode.None);
    }

    [Fact]
    public void NonGeneric_Success_Exception_IsNull()
    {
        var result = StorageResult.Success();
        result.Exception.Should().BeNull();
    }

    [Fact]
    public void Generic_Success_ToString_ContainsValue()
    {
        var result = StorageResult<string>.Success("payload");
        result.ToString().Should().Contain("payload");
    }

    [Fact]
    public void Generic_Failure_ToString_ContainsErrorCodeAndMessage()
    {
        var result = StorageResult<string>.Failure("timed out", StorageErrorCode.Timeout);
        result.ToString().Should().Contain("Timeout").And.Contain("timed out");
    }

    [Fact]
    public void Generic_Failure_WithException_StoresException()
    {
        var ex = new TimeoutException("deadline exceeded");
        var result = StorageResult<int>.Failure("timeout", ex: ex);
        result.Exception.Should().BeSameAs(ex);
    }

    [Fact]
    public void Generic_Map_OnFailure_PropagatesException()
    {
        var ex = new InvalidOperationException("boom");
        var original = StorageResult<int>.Failure("ex", ex: ex);

        var mapped = original.Map(x => x.ToString());

        mapped.Exception.Should().BeSameAs(ex);
    }

    [Fact]
    public void Generic_Map_OnFailure_PropagatesErrorCode()
    {
        var original = StorageResult<int>.Failure("bad", StorageErrorCode.QuotaExceeded);

        var mapped = original.Map(x => x * 2);

        mapped.ErrorCode.Should().Be(StorageErrorCode.QuotaExceeded);
    }

    [Fact]
    public void Generic_Success_ErrorCode_IsNone()
    {
        var result = StorageResult<int>.Success(1);
        result.ErrorCode.Should().Be(StorageErrorCode.None);
    }

    [Fact]
    public void Generic_Success_Exception_IsNull()
    {
        var result = StorageResult<string>.Success("ok");
        result.Exception.Should().BeNull();
    }
}

// ─── PipelineConfigurator ─────────────────────────────────────────────────────

public sealed class PipelineConfiguratorTests
{
    private static (IServiceCollection Services, PipelineConfigurator Configurator) Build()
    {
        var services = new ServiceCollection();
        var configurator = new PipelineConfigurator(services);
        return (services, configurator);
    }

    private static bool HasMiddleware<TMiddleware>(IServiceCollection services)
        where TMiddleware : IStorageMiddleware
        => services.Any(d =>
            d.ServiceType == typeof(IStorageMiddleware) &&
            d.ImplementationType == typeof(TMiddleware));

    private static int CountMiddleware<TMiddleware>(IServiceCollection services)
        where TMiddleware : IStorageMiddleware
        => services.Count(d =>
            d.ServiceType == typeof(IStorageMiddleware) &&
            d.ImplementationType == typeof(TMiddleware));

    [Fact]
    public void UseValidation_RegistersValidationMiddleware()
    {
        var (services, configurator) = Build();
        configurator.UseValidation();
        HasMiddleware<ValidationMiddleware>(services).Should().BeTrue();
    }

    [Fact]
    public void UseValidation_ReturnsSameConfiguratorInstance()
    {
        var (_, configurator) = Build();
        configurator.UseValidation().Should().BeSameAs(configurator);
    }

    [Fact]
    public void UseValidation_CalledTwice_RegistersOnlyOnce()
    {
        var (services, configurator) = Build();
        configurator.UseValidation().UseValidation();
        CountMiddleware<ValidationMiddleware>(services).Should().Be(1);
    }

    [Fact]
    public void UseValidation_WithConfigure_AppliesOptions()
    {
        var (services, configurator) = Build();
        configurator.UseValidation(opts => opts.MaxFileSizeBytes = 1024);
        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ValidationOptions>>()
            .Value.MaxFileSizeBytes.Should().Be(1024);
    }

    [Fact]
    public void UseValidation_WithNullConfigure_DoesNotThrow()
    {
        var (_, configurator) = Build();
        ((Action)(() => configurator.UseValidation(null))).Should().NotThrow();
    }

    [Fact]
    public void UseValidation_RegistrationLifetime_IsTransient()
    {
        var (services, configurator) = Build();
        configurator.UseValidation();
        services.First(d =>
            d.ServiceType == typeof(IStorageMiddleware) &&
            d.ImplementationType == typeof(ValidationMiddleware))
            .Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void UseCompression_RegistersCompressionMiddleware()
    {
        var (services, configurator) = Build();
        configurator.UseCompression();
        HasMiddleware<CompressionMiddleware>(services).Should().BeTrue();
    }

    [Fact]
    public void UseCompression_ReturnsSameConfiguratorInstance()
    {
        var (_, configurator) = Build();
        configurator.UseCompression().Should().BeSameAs(configurator);
    }

    [Fact]
    public void UseCompression_CalledTwice_RegistersOnlyOnce()
    {
        var (services, configurator) = Build();
        configurator.UseCompression().UseCompression();
        CountMiddleware<CompressionMiddleware>(services).Should().Be(1);
    }

    [Fact]
    public void UseCompression_WithConfigure_AppliesOptions()
    {
        var (services, configurator) = Build();
        configurator.UseCompression(opts => opts.Enabled = false);
        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CompressionOptions>>()
            .Value.Enabled.Should().BeFalse();
    }

    [Fact]
    public void UseCompression_WithNullConfigure_DoesNotThrow()
    {
        var (_, configurator) = Build();
        ((Action)(() => configurator.UseCompression(null))).Should().NotThrow();
    }

    [Fact]
    public void UseCompression_RegistrationLifetime_IsTransient()
    {
        var (services, configurator) = Build();
        configurator.UseCompression();
        services.First(d =>
            d.ServiceType == typeof(IStorageMiddleware) &&
            d.ImplementationType == typeof(CompressionMiddleware))
            .Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void UseEncryption_RegistersEncryptionMiddleware()
    {
        var (services, configurator) = Build();
        configurator.UseEncryption();
        HasMiddleware<EncryptionMiddleware>(services).Should().BeTrue();
    }

    [Fact]
    public void UseEncryption_ReturnsSameConfiguratorInstance()
    {
        var (_, configurator) = Build();
        configurator.UseEncryption().Should().BeSameAs(configurator);
    }

    [Fact]
    public void UseEncryption_CalledTwice_RegistersOnlyOnce()
    {
        var (services, configurator) = Build();
        configurator.UseEncryption().UseEncryption();
        CountMiddleware<EncryptionMiddleware>(services).Should().Be(1);
    }

    [Fact]
    public void UseEncryption_WithConfigure_AppliesOptions()
    {
        var (services, configurator) = Build();
        configurator.UseEncryption(opts => opts.Enabled = true);
        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EncryptionOptions>>()
            .Value.Enabled.Should().BeTrue();
    }

    [Fact]
    public void UseEncryption_WithNullConfigure_DoesNotThrow()
    {
        var (_, configurator) = Build();
        ((Action)(() => configurator.UseEncryption(null))).Should().NotThrow();
    }

    [Fact]
    public void UseEncryption_RegistrationLifetime_IsTransient()
    {
        var (services, configurator) = Build();
        configurator.UseEncryption();
        services.First(d =>
            d.ServiceType == typeof(IStorageMiddleware) &&
            d.ImplementationType == typeof(EncryptionMiddleware))
            .Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void ChainingAll_RegistersAllThreeMiddlewares()
    {
        var (services, configurator) = Build();

        configurator.UseValidation().UseCompression().UseEncryption();

        HasMiddleware<ValidationMiddleware>(services).Should().BeTrue();
        HasMiddleware<CompressionMiddleware>(services).Should().BeTrue();
        HasMiddleware<EncryptionMiddleware>(services).Should().BeTrue();
    }

    [Fact]
    public void ChainingAll_TwiceEach_RegistersEachOnlyOnce()
    {
        var (services, configurator) = Build();

        configurator
            .UseValidation().UseValidation()
            .UseCompression().UseCompression()
            .UseEncryption().UseEncryption();

        CountMiddleware<ValidationMiddleware>(services).Should().Be(1);
        CountMiddleware<CompressionMiddleware>(services).Should().Be(1);
        CountMiddleware<EncryptionMiddleware>(services).Should().Be(1);
    }
}
