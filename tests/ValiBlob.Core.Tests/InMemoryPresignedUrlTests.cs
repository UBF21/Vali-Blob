using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Testing;
using ValiBlob.Testing.Extensions;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class InMemoryPresignedUrlTests
{
    private readonly InMemoryStorageProvider _provider;

    public InMemoryPresignedUrlTests()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddValiBlob().UseInMemory();

        var sp = services.BuildServiceProvider();
        _provider = sp.GetRequiredService<InMemoryStorageProvider>();
    }

    [Fact]
    public async Task GetPresignedUploadUrlAsync_ShouldReturnSuccess()
    {
        var result = await _provider.GetPresignedUploadUrlAsync("docs/report.pdf", TimeSpan.FromHours(1));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetPresignedDownloadUrlAsync_ShouldReturnSuccess()
    {
        var result = await _provider.GetPresignedDownloadUrlAsync("docs/report.pdf", TimeSpan.FromHours(1));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetPresignedUploadUrlAsync_UrlShouldContainPath()
    {
        const string path = "images/photo.jpg";

        var result = await _provider.GetPresignedUploadUrlAsync(path, TimeSpan.FromMinutes(30));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(path);
    }

    [Fact]
    public async Task GetPresignedDownloadUrlAsync_UrlShouldContainPath()
    {
        const string path = "videos/clip.mp4";

        var result = await _provider.GetPresignedDownloadUrlAsync(path, TimeSpan.FromMinutes(30));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(path);
    }

    [Fact]
    public async Task GetPresignedUploadUrlAsync_UrlShouldContainExpiresParameter()
    {
        var result = await _provider.GetPresignedUploadUrlAsync("test/file.txt", TimeSpan.FromHours(2));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("expires=");
    }

    [Fact]
    public async Task GetPresignedDownloadUrlAsync_UrlShouldContainExpiresParameter()
    {
        var result = await _provider.GetPresignedDownloadUrlAsync("test/file.txt", TimeSpan.FromHours(2));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("expires=");
    }

    [Fact]
    public async Task GetPresignedUploadUrlAsync_UrlShouldContainUploadInPath()
    {
        var result = await _provider.GetPresignedUploadUrlAsync("my/path/file.bin", TimeSpan.FromHours(1));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("upload");
    }

    [Fact]
    public async Task GetPresignedDownloadUrlAsync_UrlShouldContainDownloadInPath()
    {
        var result = await _provider.GetPresignedDownloadUrlAsync("my/path/file.bin", TimeSpan.FromHours(1));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("download");
    }

    [Fact]
    public async Task GetPresignedUploadUrlAsync_DifferentPaths_ProduceDifferentUrls()
    {
        var result1 = await _provider.GetPresignedUploadUrlAsync("folder/a.txt", TimeSpan.FromHours(1));
        var result2 = await _provider.GetPresignedUploadUrlAsync("folder/b.txt", TimeSpan.FromHours(1));

        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        result1.Value.Should().NotBe(result2.Value);
    }

    [Fact]
    public async Task GetPresignedDownloadUrlAsync_DifferentPaths_ProduceDifferentUrls()
    {
        var result1 = await _provider.GetPresignedDownloadUrlAsync("folder/a.txt", TimeSpan.FromHours(1));
        var result2 = await _provider.GetPresignedDownloadUrlAsync("folder/b.txt", TimeSpan.FromHours(1));

        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        result1.Value.Should().NotBe(result2.Value);
    }

    [Fact]
    public async Task GetPresignedUploadUrlAsync_ExpirationTimeAffectsExpiresValue()
    {
        // Use a fixed reference time to compare — a short expiration should yield a lower timestamp
        var shortExpiry = TimeSpan.FromMinutes(5);
        var longExpiry = TimeSpan.FromHours(24);

        var shortResult = await _provider.GetPresignedUploadUrlAsync("test/file.txt", shortExpiry);
        var longResult = await _provider.GetPresignedUploadUrlAsync("test/file.txt", longExpiry);

        shortResult.IsSuccess.Should().BeTrue();
        longResult.IsSuccess.Should().BeTrue();

        // Extract the expires= values and compare them numerically
        var shortExpires = ExtractExpiresValue(shortResult.Value!);
        var longExpires = ExtractExpiresValue(longResult.Value!);

        shortExpires.Should().BeLessThan(longExpires);
    }

    private static long ExtractExpiresValue(string url)
    {
        var idx = url.IndexOf("expires=", StringComparison.Ordinal);
        if (idx < 0) throw new InvalidOperationException($"No 'expires=' found in URL: {url}");
        var valueStr = url.Substring(idx + "expires=".Length);
        // Strip any trailing query parameters
        var ampIdx = valueStr.IndexOf('&');
        if (ampIdx >= 0) valueStr = valueStr.Substring(0, ampIdx);
        return long.Parse(valueStr);
    }
}
