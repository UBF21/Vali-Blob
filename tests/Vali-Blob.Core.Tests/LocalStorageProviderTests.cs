using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Exceptions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Local;
using ValiBlob.Local.Options;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class LocalStorageProviderTests : IDisposable
{
    private readonly string _tempBasePath;
    private readonly LocalStorageProvider _provider;

    public LocalStorageProviderTests()
    {
        _tempBasePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempBasePath);

        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.Configure<LocalStorageOptions>(opts =>
        {
            opts.BasePath = _tempBasePath;
            opts.CreateIfNotExists = true;
            opts.PublicBaseUrl = "http://localhost:5000/files";
        });

        services.Configure<ResilienceOptions>(opts => { });
        services.Configure<EncryptionOptions>(opts => { });

        services.AddSingleton<StoragePipelineBuilder>(sp =>
        {
            var middlewares = sp.GetServices<IStorageMiddleware>();
            var builder = new StoragePipelineBuilder();
            foreach (var m in middlewares)
                builder.Use(m);
            return builder;
        });

        services.AddSingleton<Func<string, HttpClient>>(_ => _ => new HttpClient());

        var sp = services.BuildServiceProvider();
        _provider = ActivatorUtilities.CreateInstance<LocalStorageProvider>(sp);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempBasePath))
            Directory.Delete(_tempBasePath, recursive: true);
    }

    // ─── Test 1: Upload file → file exists on disk at correct path ───────────

    [Fact]
    public async Task Upload_ShouldWriteFileToDisk()
    {
        var content = "Hello, local storage!"u8.ToArray();
        var result = await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("test", "hello.txt"),
            Content = new MemoryStream(content),
            ContentType = "text/plain",
            ContentLength = content.Length
        });

        result.IsSuccess.Should().BeTrue();
        var expectedPath = Path.Combine(_tempBasePath, "test", "hello.txt");
        File.Exists(expectedPath).Should().BeTrue();
    }

    // ─── Test 2: Upload then download → content is identical ─────────────────

    [Fact]
    public async Task UploadThenDownload_ShouldReturnIdenticalContent()
    {
        var content = "Round-trip content"u8.ToArray();
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("round-trip", "data.bin"),
            Content = new MemoryStream(content),
            ContentLength = content.Length
        });

        var result = await _provider.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From("round-trip", "data.bin")
        });

        result.IsSuccess.Should().BeTrue();
        using var ms = new MemoryStream();
        await result.Value!.CopyToAsync(ms);
        ms.ToArray().Should().BeEquivalentTo(content);
    }

    // ─── Test 3: Download non-existent file → FileNotFound ───────────────────

    [Fact]
    public async Task Download_NonExistentFile_ShouldReturnFileNotFound()
    {
        var result = await _provider.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From("does-not-exist.txt")
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(StorageErrorCode.FileNotFound);
    }

    // ─── Test 4: Delete existing file → file no longer exists ────────────────

    [Fact]
    public async Task Delete_ExistingFile_ShouldRemoveFromDisk()
    {
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("delete-me.txt"),
            Content = new MemoryStream("x"u8.ToArray())
        });

        var deleteResult = await _provider.DeleteAsync("delete-me.txt");

        deleteResult.IsSuccess.Should().BeTrue();
        var expectedPath = Path.Combine(_tempBasePath, "delete-me.txt");
        File.Exists(expectedPath).Should().BeFalse();
    }

    // ─── Test 5: Delete non-existent file → success (idempotent) ─────────────

    [Fact]
    public async Task Delete_NonExistentFile_ShouldReturnSuccessIdempotent()
    {
        var result = await _provider.DeleteAsync("never-existed.txt");
        result.IsSuccess.Should().BeTrue();
    }

    // ─── Test 6: Exists on existing file → true ──────────────────────────────

    [Fact]
    public async Task Exists_OnExistingFile_ShouldReturnTrue()
    {
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("exists", "file.txt"),
            Content = new MemoryStream("x"u8.ToArray())
        });

        var result = await _provider.ExistsAsync("exists/file.txt");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    // ─── Test 7: Exists on missing file → false ──────────────────────────────

    [Fact]
    public async Task Exists_OnMissingFile_ShouldReturnFalse()
    {
        var result = await _provider.ExistsAsync("no-such-file.txt");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    // ─── Test 8: Copy → destination file has same content ────────────────────

    [Fact]
    public async Task Copy_ShouldCreateDestinationWithSameContent()
    {
        var content = "Copy me!"u8.ToArray();
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("src", "original.txt"),
            Content = new MemoryStream(content),
            ContentLength = content.Length
        });

        var copyResult = await _provider.CopyAsync("src/original.txt", "dst/copy.txt");
        copyResult.IsSuccess.Should().BeTrue();

        var downloadResult = await _provider.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From("dst", "copy.txt")
        });
        downloadResult.IsSuccess.Should().BeTrue();

        using var ms = new MemoryStream();
        await downloadResult.Value!.CopyToAsync(ms);
        ms.ToArray().Should().BeEquivalentTo(content);
    }

    // ─── Test 9: GetMetadata → returns correct SizeBytes ─────────────────────

    [Fact]
    public async Task GetMetadata_ShouldReturnCorrectSize()
    {
        var content = "Metadata test content"u8.ToArray();
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("meta", "file.txt"),
            Content = new MemoryStream(content),
            ContentType = "text/plain",
            ContentLength = content.Length
        });

        var result = await _provider.GetMetadataAsync("meta/file.txt");

        result.IsSuccess.Should().BeTrue();
        result.Value!.SizeBytes.Should().Be(content.Length);
        result.Value.ContentType.Should().Be("text/plain");
    }

    // ─── Test 10: SetMetadata then GetMetadata → metadata persisted ──────────

    [Fact]
    public async Task SetMetadata_ThenGetMetadata_ShouldPersistValues()
    {
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("meta2", "file.txt"),
            Content = new MemoryStream("x"u8.ToArray())
        });

        var setResult = await _provider.SetMetadataAsync("meta2/file.txt", new Dictionary<string, string>
        {
            { "author", "valiblob" },
            { "version", "1.0" }
        });
        setResult.IsSuccess.Should().BeTrue();

        var getResult = await _provider.GetMetadataAsync("meta2/file.txt");
        getResult.IsSuccess.Should().BeTrue();
        getResult.Value!.CustomMetadata.Should().ContainKey("author").WhoseValue.Should().Be("valiblob");
        getResult.Value.CustomMetadata.Should().ContainKey("version").WhoseValue.Should().Be("1.0");
    }

    // ─── Test 11: ListFiles → returns uploaded files ──────────────────────────

    [Fact]
    public async Task ListFiles_ShouldReturnUploadedFiles()
    {
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("list", "a.txt"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("list", "b.txt"), Content = new MemoryStream("x"u8.ToArray()) });

        var result = await _provider.ListFilesAsync("list/");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Value.Should().Contain(e => e.Path == "list/a.txt");
        result.Value.Should().Contain(e => e.Path == "list/b.txt");
    }

    // ─── Test 12: ListFiles with prefix → only matching files ────────────────

    [Fact]
    public async Task ListFiles_WithPrefix_ShouldOnlyReturnMatchingFiles()
    {
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("prefix-test", "docs", "doc.pdf"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("prefix-test", "images", "img.jpg"), Content = new MemoryStream("x"u8.ToArray()) });

        var result = await _provider.ListFilesAsync("prefix-test/docs/");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].Path.Should().Be("prefix-test/docs/doc.pdf");
    }

    // ─── Test 13: DeleteFolder → removes all files under prefix ──────────────

    [Fact]
    public async Task DeleteFolder_ShouldRemoveAllFilesUnderPrefix()
    {
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("folder-del", "a.txt"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("folder-del", "sub", "b.txt"), Content = new MemoryStream("x"u8.ToArray()) });

        var result = await _provider.DeleteFolderAsync("folder-del");
        result.IsSuccess.Should().BeTrue();

        var folderPath = Path.Combine(_tempBasePath, "folder-del");
        Directory.Exists(folderPath).Should().BeFalse();
    }

    // ─── Test 14: Resumable upload: start → 2 chunks → complete → assembled ──

    [Fact]
    public async Task ResumableUpload_StartChunksComplete_ShouldAssembleCorrectContent()
    {
        var chunk1 = "Hello, "u8.ToArray();
        var chunk2 = "resumable world!"u8.ToArray();
        var expected = chunk1.Concat(chunk2).ToArray();

        var startResult = await _provider.StartResumableUploadAsync(new ResumableUploadRequest
        {
            Path = StoragePath.From("resumable", "assembled.txt"),
            ContentType = "text/plain",
            TotalSize = expected.Length
        });
        startResult.IsSuccess.Should().BeTrue();
        var uploadId = startResult.Value!.UploadId;

        var chunk1Result = await _provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = uploadId,
            Data = new MemoryStream(chunk1),
            Offset = 0,
            Length = chunk1.Length
        });
        chunk1Result.IsSuccess.Should().BeTrue();

        var chunk2Result = await _provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = uploadId,
            Data = new MemoryStream(chunk2),
            Offset = chunk1.Length,
            Length = chunk2.Length
        });
        chunk2Result.IsSuccess.Should().BeTrue();

        var completeResult = await _provider.CompleteResumableUploadAsync(uploadId);
        completeResult.IsSuccess.Should().BeTrue();

        var downloadResult = await _provider.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From("resumable", "assembled.txt")
        });
        downloadResult.IsSuccess.Should().BeTrue();

        using var ms = new MemoryStream();
        await downloadResult.Value!.CopyToAsync(ms);
        ms.ToArray().Should().BeEquivalentTo(expected);
    }

    // ─── Test 15: Range download → returns only the requested byte range ──────

    [Fact]
    public async Task RangeDownload_ShouldReturnRequestedBytes()
    {
        var content = "0123456789ABCDEF"u8.ToArray();
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("range", "data.bin"),
            Content = new MemoryStream(content),
            ContentLength = content.Length
        });

        var result = await _provider.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From("range", "data.bin"),
            Range = new DownloadRange { From = 4, To = 9 }
        });

        result.IsSuccess.Should().BeTrue();
        using var ms = new MemoryStream();
        await result.Value!.CopyToAsync(ms);
        var downloaded = ms.ToArray();
        downloaded.Should().BeEquivalentTo(content[4..10]); // bytes 4..9 inclusive = indices 4,5,6,7,8,9
    }

    // ─── Test 16: ListFolders → returns correct subfolder names ──────────────

    [Fact]
    public async Task ListFolders_ShouldReturnSubfolderNames()
    {
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("lf-root", "alpha", "file.txt"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("lf-root", "beta", "file.txt"), Content = new MemoryStream("x"u8.ToArray()) });

        var result = await _provider.ListFoldersAsync("lf-root");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("alpha");
        result.Value.Should().Contain("beta");
    }

    // ─── Test 17: GetUrl with PublicBaseUrl → returns correct URL ────────────

    [Fact]
    public async Task GetUrl_WithPublicBaseUrl_ShouldReturnCorrectUrl()
    {
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("public", "file.txt"),
            Content = new MemoryStream("x"u8.ToArray())
        });

        var result = await _provider.GetUrlAsync("public/file.txt");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("http://localhost:5000/files/public/file.txt");
    }

    // ─── Test 18: GetUrl without PublicBaseUrl → returns file:// URI ──────────

    [Fact]
    public async Task GetUrl_WithoutPublicBaseUrl_ShouldReturnFileUri()
    {
        // Create a provider without a PublicBaseUrl
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<LocalStorageOptions>(opts =>
        {
            opts.BasePath = _tempBasePath;
            opts.CreateIfNotExists = false;
            opts.PublicBaseUrl = null;
        });
        services.Configure<ResilienceOptions>(opts => { });
        services.Configure<EncryptionOptions>(opts => { });
        services.AddSingleton<StoragePipelineBuilder>(sp =>
        {
            var middlewares = sp.GetServices<IStorageMiddleware>();
            var b = new StoragePipelineBuilder();
            foreach (var m in middlewares) b.Use(m);
            return b;
        });
        services.AddSingleton<Func<string, HttpClient>>(_ => _ => new HttpClient());

        var sp = services.BuildServiceProvider();
        var noUrlProvider = ActivatorUtilities.CreateInstance<LocalStorageProvider>(sp);

        await noUrlProvider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("file-uri-test.txt"),
            Content = new MemoryStream("x"u8.ToArray())
        });

        var result = await noUrlProvider.GetUrlAsync("file-uri-test.txt");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().StartWith("file://");
    }

    // ─── Security: path traversal and uploadId validation ────────────────────

    [Fact]
    public async Task GetUrl_WithPathTraversal_ShouldReturnFailure()
    {
        var result = await _provider.GetUrlAsync("../../../etc/passwd");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UploadAsync_WithPathTraversal_ShouldReturnFailure()
    {
        var request = new UploadRequest
        {
            Path = StoragePath.From("../../etc/shadow"),
            Content = new MemoryStream("x"u8.ToArray())
        };

        var result = await _provider.UploadAsync(request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UploadChunkAsync_WithMaliciousUploadId_ShouldThrow()
    {
        var fakeRequest = new ResumableChunkRequest
        {
            UploadId = "../../malicious",
            Data = new MemoryStream("x"u8.ToArray())
        };

        var act = async () => await _provider.UploadChunkAsync(fakeRequest);

        await act.Should().ThrowAsync<StorageValidationException>()
            .WithMessage("*Invalid uploadId*");
    }
    // ─── Security: chunk offset validation ───────────────────────────────────

    [Fact]
    public async Task UploadChunkAsync_WithNegativeOffset_ShouldReturnFailure()
    {
        var startResult = await _provider.StartResumableUploadAsync(new ResumableUploadRequest
        {
            Path = StoragePath.From("chunk-test.txt"),
            TotalSize = 100
        });
        startResult.IsSuccess.Should().BeTrue();

        var result = await _provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = startResult.Value!.UploadId,
            Data = new MemoryStream("x"u8.ToArray()),
            Offset = -1
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("non-negative");
    }

    [Fact]
    public async Task UploadChunkAsync_WithOffsetBeyondTotalSize_ShouldReturnFailure()
    {
        var startResult = await _provider.StartResumableUploadAsync(new ResumableUploadRequest
        {
            Path = StoragePath.From("chunk-test2.txt"),
            TotalSize = 50
        });
        startResult.IsSuccess.Should().BeTrue();

        var result = await _provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = startResult.Value!.UploadId,
            Data = new MemoryStream("x"u8.ToArray()),
            Offset = 100
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("exceeds total file size");
    }

    [Fact]
    public async Task UploadChunkAsync_WithZeroLength_ShouldReturnFailure()
    {
        var startResult = await _provider.StartResumableUploadAsync(new ResumableUploadRequest
        {
            Path = StoragePath.From("chunk-test3.txt"),
            TotalSize = 100
        });
        startResult.IsSuccess.Should().BeTrue();

        var result = await _provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = startResult.Value!.UploadId,
            Data = new MemoryStream("x"u8.ToArray()),
            Offset = 0,
            Length = 0
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("positive");
    }

}
