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

public sealed class ResumableChecksumTests
{
    private static InMemoryStorageProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddValiBlob().UseInMemory();

        return services.BuildServiceProvider().GetRequiredService<InMemoryStorageProvider>();
    }

    private static string ComputeMd5Base64(byte[] data)
    {
        using var md5 = MD5.Create();
        return Convert.ToBase64String(md5.ComputeHash(data));
    }

    private static async Task<string> StartSession(
        InMemoryStorageProvider provider, long totalSize, string path = "uploads/file.bin")
    {
        var sessionResult = await provider.StartResumableUploadAsync(new ResumableUploadRequest
        {
            Path = StoragePath.From(path),
            TotalSize = totalSize,
            ContentType = "application/octet-stream"
        });

        sessionResult.IsSuccess.Should().BeTrue();
        return sessionResult.Value!.UploadId;
    }

    [Fact]
    public async Task UploadChunk_WithCorrectExpectedMd5_ShouldSucceed()
    {
        var provider = BuildProvider();
        var chunkBytes = new byte[512];
        new Random(1).NextBytes(chunkBytes);
        var correctMd5 = ComputeMd5Base64(chunkBytes);

        var uploadId = await StartSession(provider, chunkBytes.Length);

        var result = await provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = uploadId,
            Data = new MemoryStream(chunkBytes),
            Offset = 0,
            Length = chunkBytes.Length,
            ExpectedMd5 = correctMd5
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.BytesUploaded.Should().Be(chunkBytes.Length);
    }

    [Fact]
    public async Task UploadChunk_WithWrongExpectedMd5_ShouldReturnValidationFailed()
    {
        var provider = BuildProvider();
        var chunkBytes = new byte[512];
        new Random(1).NextBytes(chunkBytes);

        var uploadId = await StartSession(provider, chunkBytes.Length);

        var result = await provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = uploadId,
            Data = new MemoryStream(chunkBytes),
            Offset = 0,
            Length = chunkBytes.Length,
            ExpectedMd5 = "AAAAAAAAAAAAAAAAAAAAAA==" // intentionally wrong
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(StorageErrorCode.ValidationFailed);
        result.ErrorMessage.Should().Contain("checksum");
    }

    [Fact]
    public async Task UploadChunk_WithoutExpectedMd5_ShouldAlwaysSucceed()
    {
        var provider = BuildProvider();
        var chunkBytes = new byte[512];
        new Random(1).NextBytes(chunkBytes);

        var uploadId = await StartSession(provider, chunkBytes.Length);

        var result = await provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = uploadId,
            Data = new MemoryStream(chunkBytes),
            Offset = 0,
            Length = chunkBytes.Length
            // ExpectedMd5 not set — checksum validation skipped
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.BytesUploaded.Should().Be(chunkBytes.Length);
    }

    [Fact]
    public async Task UploadChunk_SameOffsetRetried_BytesCountedOnce()
    {
        var provider = BuildProvider();
        var chunkBytes = new byte[512];
        new Random(1).NextBytes(chunkBytes);

        var uploadId = await StartSession(provider, chunkBytes.Length);

        // First upload at offset 0
        var first = await provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = uploadId,
            Data = new MemoryStream(chunkBytes),
            Offset = 0,
            Length = chunkBytes.Length
        });

        first.IsSuccess.Should().BeTrue();
        first.Value!.BytesUploaded.Should().Be(512);

        // Retry the same chunk at the same offset
        var retry = await provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = uploadId,
            Data = new MemoryStream(chunkBytes),
            Offset = 0,
            Length = chunkBytes.Length
        });

        retry.IsSuccess.Should().BeTrue();
        // BytesUploaded must not double-count the same offset
        retry.Value!.BytesUploaded.Should().Be(512);
    }

    [Fact]
    public async Task UploadChunk_RetryWithDifferentData_BufferUpdated()
    {
        var provider = BuildProvider();
        var totalSize = 512;

        var uploadId = await StartSession(provider, totalSize, "uploads/retry-data.bin");

        var firstBytes = new byte[totalSize];
        Array.Fill(firstBytes, (byte)0xAA);

        var secondBytes = new byte[totalSize];
        Array.Fill(secondBytes, (byte)0xBB);

        // Upload first version at offset 0
        await provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = uploadId,
            Data = new MemoryStream(firstBytes),
            Offset = 0,
            Length = totalSize
        });

        // Retry with different data at same offset
        await provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = uploadId,
            Data = new MemoryStream(secondBytes),
            Offset = 0,
            Length = totalSize
        });

        // Complete and verify the assembled content uses the latest data
        var completeResult = await provider.CompleteResumableUploadAsync(uploadId);
        completeResult.IsSuccess.Should().BeTrue();

        var stored = provider.GetRawBytes("uploads/retry-data.bin");
        stored.Should().BeEquivalentTo(secondBytes);
    }

    [Fact]
    public async Task ChunkedUpload_OutOfOrderChunks_AssemblesCorrectly()
    {
        var provider = BuildProvider();
        var chunk1 = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var chunk2 = new byte[] { 0x05, 0x06, 0x07, 0x08 };
        var totalSize = chunk1.Length + chunk2.Length;
        var path = "uploads/out-of-order.bin";

        var uploadId = await StartSession(provider, totalSize, path);

        // Upload chunk 2 first (offset 4)
        var r2 = await provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = uploadId,
            Data = new MemoryStream(chunk2),
            Offset = 4,
            Length = chunk2.Length
        });
        r2.IsSuccess.Should().BeTrue();

        // Then upload chunk 1 (offset 0)
        var r1 = await provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = uploadId,
            Data = new MemoryStream(chunk1),
            Offset = 0,
            Length = chunk1.Length
        });
        r1.IsSuccess.Should().BeTrue();

        // Complete and verify assembly order
        var complete = await provider.CompleteResumableUploadAsync(uploadId);
        complete.IsSuccess.Should().BeTrue();

        var stored = provider.GetRawBytes(path);
        var expected = chunk1.Concat(chunk2).ToArray();
        stored.Should().BeEquivalentTo(expected);
    }
}
