using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Models;
using ValiBlob.Testing;
using ValiBlob.Testing.Extensions;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class ValidationMiddlewareTests
{
    private static InMemoryStorageProvider BuildProviderWithValidation(
        Action<Options.ValidationOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddValiBlob()
            .UseInMemory()
            .WithPipeline(p => p.UseValidation(configure));

        return services.BuildServiceProvider().GetRequiredService<InMemoryStorageProvider>();
    }

    private static InMemoryStorageProvider BuildProviderNoValidation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddValiBlob().UseInMemory();

        return services.BuildServiceProvider().GetRequiredService<InMemoryStorageProvider>();
    }

    [Fact]
    public async Task PathTraversal_DoubleDot_ShouldReturnValidationFailed()
    {
        var provider = BuildProviderWithValidation();

        var result = await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("docs/../../../etc/passwd"),
            Content = new MemoryStream(new byte[] { 1, 2, 3 }),
            ContentType = "text/plain",
            ContentLength = 3
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("..");
    }

    [Fact]
    public async Task PathTraversal_BackslashVariant_ShouldReturnValidationFailed()
    {
        var provider = BuildProviderWithValidation();

        // StoragePath.From with a raw string containing ".." — supply a single-segment raw path
        var result = await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("..\\..\\windows\\system32\\file.txt"),
            Content = new MemoryStream(new byte[] { 1, 2, 3 }),
            ContentType = "text/plain",
            ContentLength = 3
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("..");
    }

    [Fact]
    public async Task PathTraversal_RelativeDoubleDot_ShouldReturnValidationFailed()
    {
        var provider = BuildProviderWithValidation();

        var result = await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("folder/.."),
            Content = new MemoryStream(new byte[] { 1 }),
            ContentType = "text/plain",
            ContentLength = 1
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("..");
    }

    [Fact]
    public async Task FileSizeExceedsMax_ShouldReturnValidationFailed()
    {
        var provider = BuildProviderWithValidation(v =>
        {
            v.MaxFileSizeBytes = 1024;
        });

        var bigContent = new byte[2048];
        var result = await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("uploads/bigfile.bin"),
            Content = new MemoryStream(bigContent),
            ContentType = "application/octet-stream",
            ContentLength = bigContent.Length
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("exceeds maximum");
    }

    [Fact]
    public async Task FileSizeWithinMax_ShouldSucceed()
    {
        var provider = BuildProviderWithValidation(v =>
        {
            v.MaxFileSizeBytes = 1024;
        });

        var smallContent = new byte[512];
        var result = await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("uploads/smallfile.bin"),
            Content = new MemoryStream(smallContent),
            ContentType = "application/octet-stream",
            ContentLength = smallContent.Length
        });

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AllowedExtensions_ValidExtension_ShouldSucceed()
    {
        var provider = BuildProviderWithValidation(v =>
        {
            v.MaxFileSizeBytes = 10 * 1024 * 1024;
            v.AllowedExtensions = new List<string> { ".pdf", ".jpg" };
            v.BlockedExtensions = new List<string>();
        });

        var content = new byte[100];
        var result = await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("uploads/document.pdf"),
            Content = new MemoryStream(content),
            ContentType = "application/pdf",
            ContentLength = content.Length
        });

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AllowedExtensions_InvalidExtension_ShouldReturnValidationFailed()
    {
        var provider = BuildProviderWithValidation(v =>
        {
            v.MaxFileSizeBytes = 10 * 1024 * 1024;
            v.AllowedExtensions = new List<string> { ".pdf", ".jpg" };
            v.BlockedExtensions = new List<string>();
        });

        var content = new byte[100];
        var result = await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("uploads/script.js"),
            Content = new MemoryStream(content),
            ContentType = "application/javascript",
            ContentLength = content.Length
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not allowed");
    }

    [Fact]
    public async Task BlockedExtension_ShouldReturnValidationFailed()
    {
        var provider = BuildProviderWithValidation(v =>
        {
            v.MaxFileSizeBytes = 10 * 1024 * 1024;
            v.AllowedExtensions = new List<string>();
            v.BlockedExtensions = new List<string> { ".exe" };
        });

        var content = new byte[100];
        var result = await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("uploads/malware.exe"),
            Content = new MemoryStream(content),
            ContentType = "application/octet-stream",
            ContentLength = content.Length
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("blocked");
    }

    [Fact]
    public async Task AllowedContentTypes_ValidType_ShouldSucceed()
    {
        var provider = BuildProviderWithValidation(v =>
        {
            v.MaxFileSizeBytes = 10 * 1024 * 1024;
            v.AllowedExtensions = new List<string>();
            v.BlockedExtensions = new List<string>();
            v.AllowedContentTypes = new List<string> { "text/plain", "application/pdf" };
        });

        var content = new byte[100];
        var result = await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("uploads/readme.txt"),
            Content = new MemoryStream(content),
            ContentType = "text/plain",
            ContentLength = content.Length
        });

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AllowedContentTypes_InvalidType_ShouldReturnValidationFailed()
    {
        var provider = BuildProviderWithValidation(v =>
        {
            v.MaxFileSizeBytes = 10 * 1024 * 1024;
            v.AllowedExtensions = new List<string>();
            v.BlockedExtensions = new List<string>();
            v.AllowedContentTypes = new List<string> { "text/plain", "application/pdf" };
        });

        var content = new byte[100];
        var result = await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("uploads/photo.png"),
            Content = new MemoryStream(content),
            ContentType = "image/png",
            ContentLength = content.Length
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not allowed");
    }

    [Fact]
    public async Task NoValidationConfigured_AnyFile_ShouldPass()
    {
        var provider = BuildProviderNoValidation();

        var content = new byte[10 * 1024 * 1024]; // 10 MB, no size limit configured
        var result = await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("uploads/anything.bin"),
            Content = new MemoryStream(content),
            ContentType = "application/octet-stream",
            ContentLength = content.Length
        });

        result.IsSuccess.Should().BeTrue();
    }
}
