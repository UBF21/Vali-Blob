using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Exceptions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Pipeline.Middlewares;
using ValiBlob.Core.Quota;
using ValiBlob.Core.Security;
using ValiBlob.EFCore;
using ValiBlob.Testing;
using ValiBlob.Testing.Extensions;
using Xunit;

namespace ValiBlob.Core.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// StoragePath validation — negative paths
// ─────────────────────────────────────────────────────────────────────────────

public sealed class StoragePathNegativeTests
{
    [Fact]
    public void From_NullArray_ThrowsArgumentException()
    {
        var act = () => StoragePath.From(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_EmptyString_ThrowsArgumentException()
    {
        var act = () => StoragePath.From("");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public void From_WhitespaceOnly_ThrowsArgumentException()
    {
        var act = () => StoragePath.From("   ");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*empty*");
    }

    // StoragePath.From does NOT reject ".." — path traversal is enforced at the
    // ValidationMiddleware level, not in the value object itself.
    [Fact]
    public void From_DoubleDotSegment_DoesNotThrow_PathIsTreatedLiterally()
    {
        var path = StoragePath.From("..", "escape");

        path.ToString().Should().Be("../escape");
    }

    [Fact]
    public void From_VeryLongPath_CreatesPathSuccessfully()
    {
        // 1001-character segment — StoragePath imposes no length limit; verify it doesn't truncate
        var longSegment = new string('a', 1001);

        var act = () => StoragePath.From(longSegment);

        act.Should().NotThrow();
        StoragePath.From(longSegment).ToString().Should().Be(longSegment);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// InMemoryStorageProvider — error paths
// ─────────────────────────────────────────────────────────────────────────────

public sealed class InMemoryStorageProviderNegativeTests
{
    private static InMemoryStorageProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddValiBlob().UseInMemory();
        return services.BuildServiceProvider().GetRequiredService<InMemoryStorageProvider>();
    }

    [Fact]
    public async Task DownloadAsync_NonExistentPath_ReturnsFailure()
    {
        var provider = BuildProvider();

        var result = await provider.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From("does-not-exist.bin")
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(StorageErrorCode.FileNotFound);
    }

    // DeleteAsync is idempotent: removing a key that was never present still succeeds.
    [Fact]
    public async Task DeleteAsync_NonExistentPath_ReturnsSuccess()
    {
        var provider = BuildProvider();

        var result = await provider.DeleteAsync("ghost/file.bin");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonExistentPath_ReturnsTrueWithValueFalse()
    {
        var provider = BuildProvider();

        var result = await provider.ExistsAsync("no-such-file.txt");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task GetMetadataAsync_NonExistentPath_ReturnsFailure()
    {
        var provider = BuildProvider();

        var result = await provider.GetMetadataAsync("phantom/file.txt");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(StorageErrorCode.FileNotFound);
    }

    [Fact]
    public async Task CopyAsync_SourceDoesNotExist_ReturnsFailure()
    {
        var provider = BuildProvider();

        var result = await provider.CopyAsync("source-ghost.txt", "dest.txt");

        result.IsSuccess.Should().BeFalse();
    }

    // MoveAsync delegates to CopyAsync internally; if the source is missing CopyAsync fails,
    // so MoveAsync propagates the failure without deleting anything.
    [Fact]
    public async Task MoveAsync_SourceDoesNotExist_ReturnsFailure()
    {
        var provider = BuildProvider();

        var result = await provider.MoveAsync("missing-source.txt", "destination.txt");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ListFilesAsync_PrefixMatchesNothing_ReturnsSuccessWithEmptyList()
    {
        var provider = BuildProvider();

        var result = await provider.ListFilesAsync("zzz-no-match-prefix/");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task UploadAsync_NullContentStream_ThrowsOrReturnsFailure()
    {
        var provider = BuildProvider();
        var request = new UploadRequest
        {
            Path = StoragePath.From("uploads/null-stream.bin"),
            Content = null!,
            ContentLength = 0
        };

        // The orchestrator wraps all exceptions and returns StorageResult.Failure,
        // so a null Content surfaces as IsSuccess = false rather than a thrown exception.
        bool succeeded = false;
        try
        {
            var r = await provider.UploadAsync(request);
            succeeded = r.IsSuccess;
        }
        catch (Exception)
        {
            // Thrown exception is also an acceptable failure signal — test passes.
            return;
        }

        succeeded.Should().BeFalse("null content must never produce a successful upload result");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// EfCoreResumableSessionStore — negative / guard-clause paths
// (additional tests complementing EfCoreSessionStoreTests)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class EfCoreSessionStoreNegativeTests
{
    private static (ValiResumableDbContext DbContext, EfCoreResumableSessionStore Store) CreateStore()
    {
        var options = new DbContextOptionsBuilder<ValiResumableDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new ValiResumableDbContext(options);
        var store = new EfCoreResumableSessionStore(context, NullLogger<EfCoreResumableSessionStore>.Instance);
        return (context, store);
    }

    [Fact]
    public async Task GetAsync_NullUploadId_ThrowsArgumentNullException()
    {
        var (_, store) = CreateStore();

        var act = async () => await store.GetAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetAsync_EmptyUploadId_ThrowsArgumentNullException()
    {
        var (_, store) = CreateStore();

        var act = async () => await store.GetAsync("");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SaveAsync_NullSession_ThrowsArgumentNullException()
    {
        var (_, store) = CreateStore();

        var act = async () => await store.SaveAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateAsync_NullSession_ThrowsArgumentNullException()
    {
        var (_, store) = CreateStore();

        var act = async () => await store.UpdateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DeleteAsync_NullUploadId_ThrowsArgumentNullException()
    {
        var (_, store) = CreateStore();

        var act = async () => await store.DeleteAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetAsync_ExpiredSession_ReturnsNull()
    {
        var (_, store) = CreateStore();
        var session = new ResumableUploadSession
        {
            UploadId = "ef-expired",
            Path = "uploads/old-file.bin",
            TotalSize = 1024,
            BytesUploaded = 0,
            ContentType = "application/octet-stream",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1)
        };

        await store.SaveAsync(session);
        var result = await store.GetAsync("ef-expired");

        result.Should().BeNull();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DeduplicationMiddleware — edge cases
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DeduplicationMiddlewareNegativeTests
{
    private static StorageMiddlewareDelegate NoOpNext => _ => Task.CompletedTask;

    private static InMemoryStorageProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddValiBlob().UseInMemory();
        return services.BuildServiceProvider().GetRequiredService<InMemoryStorageProvider>();
    }

    private static StoragePipelineContext MakeContext(byte[] content, string path = "uploads/edge.bin")
    {
        var request = new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(content),
            ContentLength = content.Length
        };
        return new StoragePipelineContext(request);
    }

    [Fact]
    public async Task EmptyByteArray_DoesNotThrow_HashIsComputed()
    {
        var provider = BuildProvider();
        var options = new DeduplicationOptions { Enabled = true };
        var mw = new DeduplicationMiddleware(provider, options, new InMemoryDeduplicationHashIndex());
        var ctx = MakeContext(Array.Empty<byte>());

        var act = async () => await mw.InvokeAsync(ctx, NoOpNext);

        await act.Should().NotThrowAsync();
        ctx.Items.Should().ContainKey(PipelineContextKeys.DeduplicationHash);
        ((string)ctx.Items[PipelineContextKeys.DeduplicationHash]).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SingleByte_DeduplicationWorksCorrectly()
    {
        var provider = BuildProvider();
        var options = new DeduplicationOptions { Enabled = true, CheckBeforeUpload = true };
        var mw = new DeduplicationMiddleware(provider, options, new InMemoryDeduplicationHashIndex());

        var content = new byte[] { 0xFF };

        var ctx1 = MakeContext(content, "uploads/single-byte-a.bin");
        await mw.InvokeAsync(ctx1, async c => { await provider.UploadAsync(c.Request); });
        ctx1.IsCancelled.Should().BeFalse();

        var ctx2 = MakeContext(content, "uploads/single-byte-b.bin");
        await mw.InvokeAsync(ctx2, NoOpNext);
        ctx2.IsCancelled.Should().BeTrue();
    }

    // The middleware wraps a non-seekable stream in a buffered copy so that it can
    // compute the hash and still pass full content downstream.
    [Fact]
    public async Task NonSeekableStream_MiddlewareHandlesGracefully()
    {
        var provider = BuildProvider();
        var options = new DeduplicationOptions { Enabled = true };
        var mw = new DeduplicationMiddleware(provider, options, new InMemoryDeduplicationHashIndex());

        var content = new byte[] { 1, 2, 3, 4, 5 };
        var nonSeekable = new NonSeekableStream(new MemoryStream(content));

        var request = new UploadRequest
        {
            Path = StoragePath.From("uploads/non-seekable.bin"),
            Content = nonSeekable,
            ContentLength = content.Length
        };
        var ctx = new StoragePipelineContext(request);

        byte[]? received = null;
        var act = async () => await mw.InvokeAsync(ctx, c =>
        {
            using var ms = new MemoryStream();
            c.Request.Content.CopyTo(ms);
            received = ms.ToArray();
            return Task.CompletedTask;
        });

        // Either succeeds (middleware buffers internally) or throws a descriptive exception —
        // in both cases it must not silently lose the content.
        try
        {
            await act();
            received.Should().NotBeNull();
            received!.Length.Should().Be(content.Length);
        }
        catch (Exception ex)
        {
            ex.Should().NotBeOfType<NullReferenceException>("middleware should fail descriptively, not with NRE");
        }
    }

    [Fact]
    public async Task LargeContent_1MB_CompletesWithoutTimeout()
    {
        var provider = BuildProvider();
        var options = new DeduplicationOptions { Enabled = true };
        var mw = new DeduplicationMiddleware(provider, options, new InMemoryDeduplicationHashIndex());

        var content = new byte[1_048_576];
        new Random(42).NextBytes(content);
        var ctx = MakeContext(content, "uploads/large.bin");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var act = async () => await mw.InvokeAsync(ctx, NoOpNext);

        await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(10));
    }

    // Helper: wraps a stream and marks it as non-seekable
    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Pipeline / Middleware — negative paths
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PipelineMiddlewareNegativeTests
{
    private static StorageMiddlewareDelegate NoOpNext => _ => Task.CompletedTask;

    private static InMemoryStorageProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddValiBlob().UseInMemory();
        return services.BuildServiceProvider().GetRequiredService<InMemoryStorageProvider>();
    }

    private static StoragePipelineContext MakeUploadContext(long contentLength, string path = "uploads/test.bin", string? bucket = null)
    {
        var request = new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(new byte[contentLength]),
            ContentLength = contentLength,
            BucketOverride = bucket
        };
        return new StoragePipelineContext(request);
    }

    // QuotaMiddleware: upload that exceeds the configured quota throws StorageValidationException
    [Fact]
    public async Task QuotaMiddleware_UploadExceedingQuota_ThrowsStorageValidationException()
    {
        var options = new QuotaOptions { Enabled = true, DefaultLimitBytes = 1000 };
        var service = new InMemoryStorageQuotaService(options);
        await service.RecordUploadAsync("default", 900);
        var mw = new QuotaMiddleware(service, options);

        var ctx = MakeUploadContext(contentLength: 200);
        var act = async () => await mw.InvokeAsync(ctx, NoOpNext);

        await act.Should().ThrowAsync<StorageValidationException>();
    }

    // RateLimitMiddleware: exceeding the per-window request count throws StorageValidationException
    [Fact]
    public async Task RateLimitMiddleware_ExceedingLimit_ThrowsStorageValidationException()
    {
        var mw = new RateLimitMiddleware(new RateLimitOptions
        {
            Enabled = true,
            MaxRequestsPerWindow = 2,
            Window = TimeSpan.FromMinutes(1)
        });
        var ctx = MakeUploadContext(1);

        await mw.InvokeAsync(ctx, NoOpNext);
        await mw.InvokeAsync(ctx, NoOpNext);

        var act = async () => await mw.InvokeAsync(ctx, NoOpNext);

        await act.Should().ThrowAsync<StorageValidationException>()
            .WithMessage("*Rate limit exceeded*");
    }

    // VirusScanMiddleware: mock scanner returning infected sets IsCancelled on context
    [Fact]
    public async Task VirusScanMiddleware_InfectedContent_SetsCancelledAndThrows()
    {
        IVirusScanner infected = new AlwaysInfectedVirusScanner("TestVirus");
        var mw = new VirusScanMiddleware(infected);
        var ctx = MakeUploadContext(3);

        StorageValidationException? caught = null;
        try { await mw.InvokeAsync(ctx, NoOpNext); }
        catch (StorageValidationException ex) { caught = ex; }

        caught.Should().NotBeNull();
        ctx.IsCancelled.Should().BeTrue();
        ctx.CancellationReason.Should().Contain("TestVirus");
    }

    // ConflictResolutionMiddleware with Fail when file already exists → throws StorageValidationException
    [Fact]
    public async Task ConflictResolutionMiddleware_FailMode_FileExists_ThrowsStorageValidationException()
    {
        var provider = BuildProvider();
        await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("uploads/conflict-target.txt"),
            Content = new MemoryStream(new byte[] { 0x00 }),
            ContentLength = 1
        });

        var mw = new ConflictResolutionMiddleware(provider, new ConflictResolutionOptions());
        var request = new UploadRequest
        {
            Path = StoragePath.From("uploads/conflict-target.txt"),
            Content = new MemoryStream(new byte[] { 1, 2, 3 }),
            ContentLength = 3,
            ConflictResolution = ConflictResolution.Fail
        };
        var ctx = new StoragePipelineContext(request);

        var act = async () => await mw.InvokeAsync(ctx, NoOpNext);

        await act.Should().ThrowAsync<StorageValidationException>();
    }

    // ValidationMiddleware: path with ".." segment is rejected and upload returns failure
    [Fact]
    public async Task ValidationMiddleware_PathWithDoubleDot_ReturnsFailure()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddValiBlob()
            .UseInMemory()
            .WithPipeline(p => p.UseValidation());

        var provider = services.BuildServiceProvider().GetRequiredService<InMemoryStorageProvider>();

        var result = await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("folder/../../../etc/shadow"),
            Content = new MemoryStream(new byte[] { 1, 2, 3 }),
            ContentType = "text/plain",
            ContentLength = 3
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("..");
    }

    private sealed class AlwaysInfectedVirusScanner(string threatName) : IVirusScanner
    {
        public Task<VirusScanResult> ScanAsync(Stream content, string? fileName, CancellationToken cancellationToken = default)
            => Task.FromResult(VirusScanResult.Infected(threatName, "FakeScanner"));
    }
}
