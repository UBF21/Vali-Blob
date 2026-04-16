using FluentAssertions;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Cdn;
using ValiBlob.Core.Options;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class PrefixCdnProviderTests
{
    private static PrefixCdnProvider Create(string baseUrl) =>
        new(Microsoft.Extensions.Options.Options.Create(new CdnOptions { BaseUrl = baseUrl }));

    [Fact]
    public void GetCdnUrl_CombinesBaseUrlAndPath()
    {
        var provider = Create("https://cdn.example.com");
        var result = provider.GetCdnUrl("images/photo.jpg");

        result.Should().Be("https://cdn.example.com/images/photo.jpg");
    }

    [Fact]
    public void GetCdnUrl_BaseUrlWithTrailingSlash_NoDoubleSlash()
    {
        var provider = Create("https://cdn.example.com/");
        var result = provider.GetCdnUrl("images/photo.jpg");

        result.Should().NotContain("//images");
        result.Should().Be("https://cdn.example.com/images/photo.jpg");
    }

    [Fact]
    public void GetCdnUrl_PathWithLeadingSlash_NoDoubleSlash()
    {
        var provider = Create("https://cdn.example.com");
        var result = provider.GetCdnUrl("/images/photo.jpg");

        result.Should().NotContain("com//images");
        result.Should().Be("https://cdn.example.com/images/photo.jpg");
    }

    [Fact]
    public async Task InvalidateCacheAsync_CompletesWithoutThrowing()
    {
        var provider = Create("https://cdn.example.com");

        var act = async () => await provider.InvalidateCacheAsync("images/photo.jpg");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void GetCdnUrl_EmptyBaseUrl_ReturnsJustPath()
    {
        var provider = Create(string.Empty);
        var result = provider.GetCdnUrl("images/photo.jpg");

        // baseUrl.TrimEnd('/') = "", so result = "/images/photo.jpg"
        result.Should().Be("/images/photo.jpg");
    }

    [Fact]
    public void GetCdnUrl_NestedPath_AllSegmentsPreserved()
    {
        var provider = Create("https://cdn.example.com");
        var result = provider.GetCdnUrl("a/b/c/d/file.pdf");

        result.Should().Be("https://cdn.example.com/a/b/c/d/file.pdf");
    }

    [Fact]
    public void GetCdnUrl_TwoDifferentPaths_ProduceTwoDifferentUrls()
    {
        var provider = Create("https://cdn.example.com");
        var url1 = provider.GetCdnUrl("images/alpha.jpg");
        var url2 = provider.GetCdnUrl("images/beta.jpg");

        url1.Should().NotBe(url2);
    }

    [Fact]
    public void GetCdnUrl_BothTrailingAndLeadingSlash_NoDoubleSlash()
    {
        var provider = Create("https://cdn.example.com/");
        var result = provider.GetCdnUrl("/assets/logo.png");

        result.Should().NotContain("//assets");
        result.Should().Be("https://cdn.example.com/assets/logo.png");
    }
}
