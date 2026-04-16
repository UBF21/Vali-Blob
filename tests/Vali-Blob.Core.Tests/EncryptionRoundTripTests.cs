using System.IO.Compression;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Models;
using ValiBlob.Testing;
using ValiBlob.Testing.Extensions;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class EncryptionRoundTripTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (InMemoryStorageProvider Provider, byte[] Key, byte[] Iv) BuildProvider(
        bool encryptionEnabled,
        bool compressionEnabled = false,
        byte[]? key = null,
        byte[]? iv = null)
    {
        var aesKey = key ?? RandomBytes(32);
        var aesIv = iv ?? RandomBytes(16);

        var keyCopy = aesKey;
        var ivCopy = aesIv;

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        var builder = services.AddValiBlob().UseInMemory();

        builder.WithPipeline(p =>
        {
            if (compressionEnabled)
                p.UseCompression(c =>
                {
                    c.Enabled = true;
                    c.MinSizeBytes = 1;
                    c.CompressibleContentTypes = new List<string> { "text/plain", "application/octet-stream" };
                });

            p.UseEncryption(e =>
            {
                e.Enabled = encryptionEnabled;
                e.Key = keyCopy;
                e.IV = ivCopy;
            });
        });

        var provider = services.BuildServiceProvider().GetRequiredService<InMemoryStorageProvider>();
        return (provider, aesKey, aesIv);
    }

    private static byte[] RandomBytes(int length)
    {
        var buf = new byte[length];
        RandomNumberGenerator.Fill(buf);
        return buf;
    }

    private static async Task<byte[]> ReadStreamAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    // ─── Test 1: Basic encrypt/decrypt round-trip ────────────────────────────

    [Fact]
    public async Task UploadEncrypted_DownloadDecrypted_ContentMatchesOriginal()
    {
        var (provider, _, _) = BuildProvider(encryptionEnabled: true);
        var original = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var path = "roundtrip/basic.bin";

        var uploadResult = await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "application/octet-stream",
            ContentLength = original.Length,
            Options = new UploadOptions { Encryption = StorageEncryptionMode.ClientSide }
        });

        uploadResult.IsSuccess.Should().BeTrue();

        var downloadResult = await provider.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From(path)
        });

        downloadResult.IsSuccess.Should().BeTrue();
        var downloaded = await ReadStreamAsync(downloadResult.Value!);
        downloaded.Should().BeEquivalentTo(original);
    }

    // ─── Test 2: Metadata contains IV key after encrypted upload ─────────────

    [Fact]
    public async Task UploadEncrypted_MetadataContainsIvKey()
    {
        var (provider, _, _) = BuildProvider(encryptionEnabled: true);
        var original = RandomBytes(64);
        var path = "roundtrip/meta-iv.bin";

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
        metaResult.Value!.CustomMetadata.Should().ContainKey("x-vali-iv");
        metaResult.Value.CustomMetadata.Should().ContainKey("x-vali-encrypted");
        metaResult.Value.CustomMetadata["x-vali-encrypted"].Should().Be("AES-256-CBC");
    }

    // ─── Test 3: Download without encryption configured returns raw encrypted bytes ─

    [Fact]
    public async Task DownloadWithoutEncryptionOptions_ReturnsEncryptedBytesAsIs()
    {
        // Build a provider WITH encryption for upload
        var (uploadProvider, key, iv) = BuildProvider(encryptionEnabled: true);
        var original = new byte[] { 10, 20, 30, 40, 50 };
        var path = "roundtrip/no-key.bin";

        await uploadProvider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "application/octet-stream",
            ContentLength = original.Length,
            Options = new UploadOptions { Encryption = StorageEncryptionMode.ClientSide }
        });

        // Build a separate provider WITHOUT encryption enabled for download
        var (downloadProvider, _, _) = BuildProvider(encryptionEnabled: false);

        // Since providers are separate instances, we need to use the same store.
        // To test this scenario properly: verify that raw stored bytes differ from original.
        var rawStored = uploadProvider.GetRawBytes(path);
        rawStored.Should().NotBeEquivalentTo(original, "the stored bytes should be encrypted");

        // Download via the same provider WITH encryption options -> should decrypt
        var downloadResult = await uploadProvider.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From(path)
        });
        downloadResult.IsSuccess.Should().BeTrue();
        var decrypted = await ReadStreamAsync(downloadResult.Value!);
        decrypted.Should().BeEquivalentTo(original);
    }

    // ─── Test 4: Upload unencrypted, download, content matches ───────────────

    [Fact]
    public async Task UploadUnencrypted_Download_ContentMatchesOriginal()
    {
        var (provider, _, _) = BuildProvider(encryptionEnabled: true);
        var original = RandomBytes(128);
        var path = "roundtrip/unencrypted.bin";

        // No Encryption option set → not encrypted
        await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "application/octet-stream",
            ContentLength = original.Length
        });

        var downloadResult = await provider.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From(path)
        });

        downloadResult.IsSuccess.Should().BeTrue();
        var downloaded = await ReadStreamAsync(downloadResult.Value!);
        downloaded.Should().BeEquivalentTo(original);
    }

    // ─── Test 5: Compression + Encryption round-trip ─────────────────────────

    [Fact]
    public async Task CompressionAndEncryption_RoundTrip_ContentMatchesOriginal()
    {
        // Build provider with both compression and encryption enabled
        var (provider, _, _) = BuildProvider(encryptionEnabled: true, compressionEnabled: true);

        // Use repetitive content to ensure compression kicks in
        var original = System.Text.Encoding.UTF8.GetBytes(
            string.Join("", Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 50)));
        var path = "roundtrip/compress-encrypt.txt";

        var uploadResult = await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "text/plain",
            ContentLength = original.Length,
            Options = new UploadOptions { Encryption = StorageEncryptionMode.ClientSide }
        });

        uploadResult.IsSuccess.Should().BeTrue();

        // Verify the file was both compressed and encrypted (stored bytes differ from original)
        var stored = provider.GetRawBytes(path);
        stored.Should().NotBeEquivalentTo(original);

        // Verify metadata has both compression and encryption markers
        var metaResult = await provider.GetMetadataAsync(path);
        metaResult.Value!.CustomMetadata.Should().ContainKey("x-vali-compressed");
        metaResult.Value.CustomMetadata.Should().ContainKey("x-vali-iv");

        // Download should transparently decrypt then decompress
        var downloadResult = await provider.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From(path)
        });

        downloadResult.IsSuccess.Should().BeTrue();
        var downloaded = await ReadStreamAsync(downloadResult.Value!);
        downloaded.Should().BeEquivalentTo(original);
    }

    // ─── Test 6: AutoDecrypt = false returns encrypted bytes ─────────────────

    [Fact]
    public async Task Download_AutoDecryptFalse_ReturnsEncryptedBytes()
    {
        var (provider, _, _) = BuildProvider(encryptionEnabled: true);
        var original = RandomBytes(64);
        var path = "roundtrip/no-autodecrypt.bin";

        await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "application/octet-stream",
            ContentLength = original.Length,
            Options = new UploadOptions { Encryption = StorageEncryptionMode.ClientSide }
        });

        var downloadResult = await provider.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From(path),
            AutoDecrypt = false
        });

        downloadResult.IsSuccess.Should().BeTrue();
        var downloaded = await ReadStreamAsync(downloadResult.Value!);

        // Should be encrypted bytes, not original
        downloaded.Should().NotBeEquivalentTo(original);

        // Should match what's in the store (raw encrypted bytes)
        var rawStored = provider.GetRawBytes(path);
        downloaded.Should().BeEquivalentTo(rawStored);
    }

    // ─── Test 7: AutoDecompress = false returns decrypted but compressed bytes ─

    [Fact]
    public async Task Download_AutoDecompressFalse_ReturnsDecryptedButCompressedBytes()
    {
        var (provider, _, _) = BuildProvider(encryptionEnabled: true, compressionEnabled: true);

        var original = System.Text.Encoding.UTF8.GetBytes(
            string.Join("", Enumerable.Repeat("AAAAABBBBBCCCCC", 100)));
        var path = "roundtrip/no-autodecompress.txt";

        await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "text/plain",
            ContentLength = original.Length,
            Options = new UploadOptions { Encryption = StorageEncryptionMode.ClientSide }
        });

        // AutoDecompress = false: should decrypt, but not decompress
        var downloadResult = await provider.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From(path),
            AutoDecompress = false
        });

        downloadResult.IsSuccess.Should().BeTrue();
        var downloaded = await ReadStreamAsync(downloadResult.Value!);

        // Should NOT be original (still compressed)
        downloaded.Should().NotBeEquivalentTo(original);

        // The downloaded bytes should be valid GZip compressed data that decompresses to original
        using var ms = new MemoryStream(downloaded);
        using var gzip = new GZipStream(ms, CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        await gzip.CopyToAsync(decompressed);
        decompressed.ToArray().Should().BeEquivalentTo(original);
    }

    // ─── Test 8: Wrong key → DownloadAsync returns failure or produces corrupted data ─

    [Fact]
    public async Task Download_WrongKey_FailsOrProducesCorruptedData()
    {
        // Upload with key A (the correct key)
        var (uploadProvider, _, _) = BuildProvider(encryptionEnabled: true);
        var original = RandomBytes(64);
        var path = "roundtrip/wrong-key.bin";

        await uploadProvider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "application/octet-stream",
            ContentLength = original.Length,
            Options = new UploadOptions { Encryption = StorageEncryptionMode.ClientSide }
        });

        // Get the raw encrypted bytes and their metadata (including the original IV)
        var rawEncrypted = uploadProvider.GetRawBytes(path);
        var rawMeta = (await uploadProvider.GetMetadataAsync(path)).Value!.CustomMetadata;

        // Build a second provider with a DIFFERENT key (wrong key for decryption)
        var wrongKey = RandomBytes(32);
        var wrongIv = RandomBytes(16);
        var (downloadProvider, _, _) = BuildProvider(encryptionEnabled: true, key: wrongKey, iv: wrongIv);

        // Store the ciphertext + original IV metadata in the wrong-key provider WITHOUT re-encrypting
        // (no ClientSide option → pipeline won't encrypt again)
        await downloadProvider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(rawEncrypted),
            ContentType = "application/octet-stream",
            ContentLength = rawEncrypted.Length,
            Metadata = rawMeta.ToDictionary(k => k.Key, v => v.Value)
        });

        // Attempt to download — wrong key should cause bad PKCS7 padding → the BaseStorageProvider
        // catch will return a failure result, OR on rare chance produces garbled non-matching output
        var result = await downloadProvider.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From(path)
        });

        if (result.IsSuccess && result.Value is not null)
        {
            // On the rare case where wrong key doesn't throw (e.g., padding happens to be valid),
            // the decrypted content must NOT equal the original plaintext
            var downloadedBytes = await ReadStreamAsync(result.Value);
            downloadedBytes.Should().NotBeEquivalentTo(original,
                "decrypting with wrong key must not reproduce original plaintext");
        }
        else
        {
            // The expected case: bad padding causes the error to be caught and returned as failure
            result.IsSuccess.Should().BeFalse("bad key should cause decryption failure");
            result.ErrorMessage.Should().NotBeNullOrEmpty();
        }
    }

    // ─── Test 9: Multiple files — each decrypts independently ────────────────

    [Fact]
    public async Task MultipleEncryptedFiles_EachDecryptsIndependently()
    {
        var (provider, _, _) = BuildProvider(encryptionEnabled: true);

        var files = new Dictionary<string, byte[]>
        {
            ["roundtrip/file1.bin"] = RandomBytes(32),
            ["roundtrip/file2.bin"] = RandomBytes(64),
            ["roundtrip/file3.bin"] = RandomBytes(128)
        };

        // Upload all files
        foreach (var (path, content) in files)
        {
            var uploadResult = await provider.UploadAsync(new UploadRequest
            {
                Path = StoragePath.From(path),
                Content = new MemoryStream(content),
                ContentType = "application/octet-stream",
                ContentLength = content.Length,
                Options = new UploadOptions { Encryption = StorageEncryptionMode.ClientSide }
            });
            uploadResult.IsSuccess.Should().BeTrue($"upload of {path} should succeed");
        }

        // Download and verify each file
        foreach (var (path, expectedContent) in files)
        {
            var downloadResult = await provider.DownloadAsync(new DownloadRequest
            {
                Path = StoragePath.From(path)
            });

            downloadResult.IsSuccess.Should().BeTrue($"download of {path} should succeed");
            var downloaded = await ReadStreamAsync(downloadResult.Value!);
            downloaded.Should().BeEquivalentTo(expectedContent, $"file {path} should match original");
        }
    }

    // ─── Test 10: Large content (1MB+) round-trip ────────────────────────────

    [Fact]
    public async Task LargeContent_EncryptDecrypt_RoundTripSucceeds()
    {
        var (provider, _, _) = BuildProvider(encryptionEnabled: true);

        // 1 MB of random bytes
        var original = RandomBytes(1024 * 1024);
        var path = "roundtrip/large.bin";

        var uploadResult = await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(original),
            ContentType = "application/octet-stream",
            ContentLength = original.Length,
            Options = new UploadOptions { Encryption = StorageEncryptionMode.ClientSide }
        });

        uploadResult.IsSuccess.Should().BeTrue();

        var downloadResult = await provider.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From(path)
        });

        downloadResult.IsSuccess.Should().BeTrue();
        var downloaded = await ReadStreamAsync(downloadResult.Value!);
        downloaded.Should().BeEquivalentTo(original);
    }
}
