using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Models;
using ValiBlob.Testing;
using ValiBlob.Testing.Extensions;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class MiddlewarePipelineTests
{
    // ─── Compression ─────────────────────────────────────────────────────────

    private static InMemoryStorageProvider BuildCompressionProvider(
        Action<Options.CompressionOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddValiBlob()
            .UseInMemory()
            .WithPipeline(p => p.UseCompression(configure));

        return services.BuildServiceProvider().GetRequiredService<InMemoryStorageProvider>();
    }

    /// <summary>
    /// Generates highly compressible text content larger than minSizeBytes.
    /// </summary>
    private static byte[] MakeRepetitiveContent(int sizeBytes)
    {
        var text = string.Join("", Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", sizeBytes / 45 + 1));
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        Array.Resize(ref bytes, sizeBytes);
        return bytes;
    }

    [Fact]
    public async Task Compression_CompressibleContentType_LargeFile_ShouldCompressContent()
    {
        var provider = BuildCompressionProvider(c =>
        {
            c.Enabled = true;
            c.MinSizeBytes = 100;
            c.CompressibleContentTypes = new List<string> { "text/plain" };
        });

        var original = MakeRepetitiveContent(2000);
        var path = "uploads/compress-test.txt";

        var uploadResult = await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "text/plain",
            ContentLength = original.Length
        });

        uploadResult.IsSuccess.Should().BeTrue();
        var stored = provider.GetRawBytes(path);

        // Compressed bytes should differ from the original input
        stored.Should().NotBeEquivalentTo(original);
    }

    [Fact]
    public async Task Compression_CompressibleContentType_StoresSmallerContent()
    {
        var provider = BuildCompressionProvider(c =>
        {
            c.Enabled = true;
            c.MinSizeBytes = 100;
            c.CompressibleContentTypes = new List<string> { "text/plain" };
        });

        // Highly repetitive text compresses very well
        var original = MakeRepetitiveContent(4000);
        var path = "uploads/compress-smaller.txt";

        await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "text/plain",
            ContentLength = original.Length
        });

        var stored = provider.GetRawBytes(path);
        stored.Length.Should().BeLessThan(original.Length);
    }

    [Fact]
    public async Task Compression_SetsCompressedMetadata()
    {
        var provider = BuildCompressionProvider(c =>
        {
            c.Enabled = true;
            c.MinSizeBytes = 100;
            c.CompressibleContentTypes = new List<string> { "text/plain" };
        });

        var original = MakeRepetitiveContent(500);
        var path = "uploads/compress-meta.txt";

        await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "text/plain",
            ContentLength = original.Length
        });

        var metaResult = await provider.GetMetadataAsync(path);
        metaResult.IsSuccess.Should().BeTrue();
        metaResult.Value!.CustomMetadata.Should().ContainKey("x-vali-compressed");
        metaResult.Value.CustomMetadata["x-vali-compressed"].Should().Be("gzip");
    }

    [Fact]
    public async Task Compression_Disabled_ShouldNotCompress()
    {
        var provider = BuildCompressionProvider(c =>
        {
            c.Enabled = false;
            c.MinSizeBytes = 100;
            c.CompressibleContentTypes = new List<string> { "text/plain" };
        });

        var original = MakeRepetitiveContent(2000);
        var path = "uploads/no-compress.txt";

        await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "text/plain",
            ContentLength = original.Length
        });

        var stored = provider.GetRawBytes(path);
        stored.Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task Compression_SmallFile_BelowMinSize_ShouldNotCompress()
    {
        var provider = BuildCompressionProvider(c =>
        {
            c.Enabled = true;
            c.MinSizeBytes = 1000;
            c.CompressibleContentTypes = new List<string> { "text/plain" };
        });

        var original = MakeRepetitiveContent(50); // 50 bytes < MinSizeBytes of 1000
        var path = "uploads/small-file.txt";

        await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "text/plain",
            ContentLength = original.Length
        });

        var stored = provider.GetRawBytes(path);
        stored.Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task Compression_NonCompressibleType_ShouldNotCompress()
    {
        var provider = BuildCompressionProvider(c =>
        {
            c.Enabled = true;
            c.MinSizeBytes = 100;
            c.CompressibleContentTypes = new List<string> { "text/plain", "application/json" };
        });

        var original = MakeRepetitiveContent(2000);
        var path = "uploads/image.png";

        await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "image/png",
            ContentLength = original.Length
        });

        var stored = provider.GetRawBytes(path);
        stored.Should().BeEquivalentTo(original);
    }

    // ─── Encryption ──────────────────────────────────────────────────────────

    private static (InMemoryStorageProvider Provider, byte[] Key, byte[] Iv) BuildEncryptionProvider(
        bool enabled, Action<Options.EncryptionOptions>? extraConfig = null)
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);
        var iv = new byte[16];
        new Random(42).NextBytes(iv);

        var keyCopy = key;
        var ivCopy = iv;

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddValiBlob()
            .UseInMemory()
            .WithPipeline(p => p.UseEncryption(e =>
            {
                e.Enabled = enabled;
                e.Key = keyCopy;
                e.IV = ivCopy;
                extraConfig?.Invoke(e);
            }));

        var provider = services.BuildServiceProvider().GetRequiredService<InMemoryStorageProvider>();
        return (provider, key, iv);
    }

    [Fact]
    public async Task Encryption_ClientSideMode_ContentDiffersFromOriginal()
    {
        var (provider, _, _) = BuildEncryptionProvider(enabled: true);

        var original = new byte[64];
        new Random(1).NextBytes(original);
        var path = "uploads/encrypted.bin";

        await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "application/octet-stream",
            ContentLength = original.Length,
            Options = new UploadOptions { Encryption = StorageEncryptionMode.ClientSide }
        });

        var stored = provider.GetRawBytes(path);
        stored.Should().NotBeEquivalentTo(original);
    }

    [Fact]
    public async Task Encryption_ClientSideMode_SetsEncryptedMetadata()
    {
        var (provider, _, _) = BuildEncryptionProvider(enabled: true);

        var original = new byte[64];
        new Random(2).NextBytes(original);
        var path = "uploads/encrypted-meta.bin";

        await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "application/octet-stream",
            ContentLength = original.Length,
            Options = new UploadOptions { Encryption = StorageEncryptionMode.ClientSide }
        });

        var metaResult = await provider.GetMetadataAsync(path);
        metaResult.IsSuccess.Should().BeTrue();
        metaResult.Value!.CustomMetadata.Should().ContainKey("x-vali-encrypted");
        metaResult.Value.CustomMetadata["x-vali-encrypted"].Should().Be("AES-256-CBC");
    }

    [Fact]
    public async Task Encryption_Disabled_ContentUnchanged()
    {
        var (provider, _, _) = BuildEncryptionProvider(enabled: false);

        var original = new byte[64];
        new Random(3).NextBytes(original);
        var path = "uploads/not-encrypted.bin";

        await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "application/octet-stream",
            ContentLength = original.Length,
            Options = new UploadOptions { Encryption = StorageEncryptionMode.ClientSide }
        });

        var stored = provider.GetRawBytes(path);
        stored.Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task Encryption_NoEncryptionMode_ContentUnchanged()
    {
        // Middleware is enabled but no Encryption mode requested — should not encrypt
        var (provider, _, _) = BuildEncryptionProvider(enabled: true);

        var original = new byte[64];
        new Random(4).NextBytes(original);
        var path = "uploads/mode-none.bin";

        await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "application/octet-stream",
            ContentLength = original.Length
            // Options is null — no Encryption requested
        });

        var stored = provider.GetRawBytes(path);
        stored.Should().BeEquivalentTo(original);
    }
}
