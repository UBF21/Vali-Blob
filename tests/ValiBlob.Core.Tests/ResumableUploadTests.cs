using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Models;
using ValiBlob.Testing;
using ValiBlob.Testing.Extensions;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class ResumableUploadTests
{
    private readonly InMemoryStorageProvider _provider;

    public ResumableUploadTests()
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
    public async Task StartResumableUpload_ShouldReturnSession()
    {
        var request = new ResumableUploadRequest
        {
            Path = StoragePath.From("videos", "large.mp4"),
            ContentType = "video/mp4",
            TotalSize = 20 * 1024 * 1024  // 20 MB
        };

        var result = await _provider.StartResumableUploadAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value!.UploadId.Should().NotBeNullOrEmpty();
        result.Value.Path.Should().Be("videos/large.mp4");
        result.Value.TotalSize.Should().Be(20 * 1024 * 1024);
        result.Value.BytesUploaded.Should().Be(0);
        result.Value.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task UploadChunk_ShouldAccumulateProgress()
    {
        const int chunkSize = 5 * 1024 * 1024;  // 5 MB
        const int totalSize = 15 * 1024 * 1024; // 15 MB = 3 chunks

        var session = (await _provider.StartResumableUploadAsync(new ResumableUploadRequest
        {
            Path = StoragePath.From("chunks", "file.bin"),
            TotalSize = totalSize
        })).Value!;

        var chunk1 = new byte[chunkSize];
        var chunkResult1 = await _provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = session.UploadId,
            Data = new MemoryStream(chunk1),
            Offset = 0,
            Length = chunkSize
        });

        chunkResult1.IsSuccess.Should().BeTrue();
        chunkResult1.Value!.BytesUploaded.Should().Be(chunkSize);
        chunkResult1.Value.IsReadyToComplete.Should().BeFalse();

        var chunk2 = new byte[chunkSize];
        await _provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = session.UploadId,
            Data = new MemoryStream(chunk2),
            Offset = chunkSize,
            Length = chunkSize
        });

        var chunk3 = new byte[chunkSize];
        var chunkResult3 = await _provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = session.UploadId,
            Data = new MemoryStream(chunk3),
            Offset = 2 * chunkSize,
            Length = chunkSize
        });

        chunkResult3.IsSuccess.Should().BeTrue();
        chunkResult3.Value!.BytesUploaded.Should().Be(totalSize);
        chunkResult3.Value.IsReadyToComplete.Should().BeTrue();
        chunkResult3.Value.ProgressPercent.Should().BeApproximately(100.0, 0.01);
    }

    [Fact]
    public async Task GetUploadStatus_ShouldReturnCurrentProgress()
    {
        const int totalSize = 10 * 1024 * 1024;
        const int chunkSize = 4 * 1024 * 1024;

        var session = (await _provider.StartResumableUploadAsync(new ResumableUploadRequest
        {
            Path = StoragePath.From("status", "file.bin"),
            TotalSize = totalSize
        })).Value!;

        await _provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = session.UploadId,
            Data = new MemoryStream(new byte[chunkSize]),
            Offset = 0,
            Length = chunkSize
        });

        var status = await _provider.GetUploadStatusAsync(session.UploadId);

        status.IsSuccess.Should().BeTrue();
        status.Value!.BytesUploaded.Should().Be(chunkSize);
        status.Value.TotalSize.Should().Be(totalSize);
        status.Value.IsComplete.Should().BeFalse();
        status.Value.ProgressPercent.Should().BeApproximately(40.0, 0.01);
    }

    [Fact]
    public async Task CompleteResumableUpload_ShouldStoreFile()
    {
        var content = new byte[1024];
        new Random(42).NextBytes(content);

        var session = (await _provider.StartResumableUploadAsync(new ResumableUploadRequest
        {
            Path = StoragePath.From("complete", "data.bin"),
            ContentType = "application/octet-stream",
            TotalSize = content.Length
        })).Value!;

        await _provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = session.UploadId,
            Data = new MemoryStream(content),
            Offset = 0
        });

        var completeResult = await _provider.CompleteResumableUploadAsync(session.UploadId);

        completeResult.IsSuccess.Should().BeTrue();
        completeResult.Value!.Path.Should().Be("complete/data.bin");
        completeResult.Value.SizeBytes.Should().Be(content.Length);

        _provider.HasFile("complete/data.bin").Should().BeTrue();
        _provider.GetRawBytes("complete/data.bin").Should().BeEquivalentTo(content);
    }

    [Fact]
    public async Task AbortResumableUpload_ShouldDiscardSession()
    {
        var session = (await _provider.StartResumableUploadAsync(new ResumableUploadRequest
        {
            Path = StoragePath.From("abort", "file.bin"),
            TotalSize = 1024
        })).Value!;

        var abortResult = await _provider.AbortResumableUploadAsync(session.UploadId);
        abortResult.IsSuccess.Should().BeTrue();

        _provider.ActiveResumableUploadIds.Should().NotContain(session.UploadId);
        _provider.HasFile("abort/file.bin").Should().BeFalse();
    }

    [Fact]
    public async Task UploadChunk_AfterAbort_ShouldReturnFailure()
    {
        var session = (await _provider.StartResumableUploadAsync(new ResumableUploadRequest
        {
            Path = StoragePath.From("abort-chunk", "file.bin"),
            TotalSize = 1024
        })).Value!;

        await _provider.AbortResumableUploadAsync(session.UploadId);

        // After abort the session is gone; uploading returns FileNotFound
        var result = await _provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = session.UploadId,
            Data = new MemoryStream(new byte[100]),
            Offset = 0
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(StorageErrorCode.FileNotFound);
    }

    [Fact]
    public async Task GetUploadStatus_UnknownSession_ShouldReturnNotFound()
    {
        var result = await _provider.GetUploadStatusAsync("nonexistent-upload-id");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(StorageErrorCode.FileNotFound);
    }

    [Fact]
    public async Task CompleteResumableUpload_PreservesContentType()
    {
        const string contentType = "video/mp4";
        var session = (await _provider.StartResumableUploadAsync(new ResumableUploadRequest
        {
            Path = StoragePath.From("media", "video.mp4"),
            ContentType = contentType,
            TotalSize = 512
        })).Value!;

        await _provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = session.UploadId,
            Data = new MemoryStream(new byte[512]),
            Offset = 0
        });

        var completeResult = await _provider.CompleteResumableUploadAsync(session.UploadId);
        completeResult.IsSuccess.Should().BeTrue();

        var metadata = await _provider.GetMetadataAsync("media/video.mp4");
        metadata.IsSuccess.Should().BeTrue();
        metadata.Value!.ContentType.Should().Be(contentType);
    }

    [Fact]
    public async Task ResumableUploadSession_ProgressPercent_IsCorrect()
    {
        var request = new ResumableUploadRequest
        {
            Path = StoragePath.From("progress", "file.bin"),
            TotalSize = 1000
        };

        var session = (await _provider.StartResumableUploadAsync(request)).Value!;

        var chunkResult = await _provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = session.UploadId,
            Data = new MemoryStream(new byte[250]),
            Offset = 0,
            Length = 250
        });

        chunkResult.Value!.ProgressPercent.Should().BeApproximately(25.0, 0.01);
        chunkResult.Value.IsReadyToComplete.Should().BeFalse();
    }
}
