using FluentAssertions;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Exceptions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Pipeline.Middlewares;
using ValiBlob.Core.Quota;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class QuotaMiddlewareTests
{
    private static StorageMiddlewareDelegate NoOpNext => _ => Task.CompletedTask;

    private static StoragePipelineContext MakeContext(
        long contentLength,
        string path = "uploads/test.bin",
        string? bucketOverride = null)
    {
        var request = new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(new byte[contentLength]),
            ContentLength = contentLength,
            BucketOverride = bucketOverride
        };
        return new StoragePipelineContext(request);
    }

    private static (QuotaMiddleware Middleware, InMemoryStorageQuotaService Service) Build(
        QuotaOptions options)
    {
        var service = new InMemoryStorageQuotaService(options);
        var mw = new QuotaMiddleware(service, options);
        return (mw, service);
    }

    // 1. When Enabled = false → passes through without quota check
    [Fact]
    public async Task Disabled_PassesThroughWithoutQuotaCheck()
    {
        var options = new QuotaOptions
        {
            Enabled = false,
            DefaultLimitBytes = 10  // tiny limit that would reject if checked
        };
        var (mw, _) = Build(options);
        var ctx = MakeContext(contentLength: 1000); // way over limit

        var nextCalled = false;
        await mw.InvokeAsync(ctx, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue();
        ctx.IsCancelled.Should().BeFalse();
    }

    // 2. Under quota → calls next successfully
    [Fact]
    public async Task UnderQuota_CallsNext()
    {
        var options = new QuotaOptions
        {
            Enabled = true,
            DefaultLimitBytes = 1_000_000
        };
        var (mw, service) = Build(options);
        await service.RecordUploadAsync("default", 100);

        var ctx = MakeContext(contentLength: 500);
        var nextCalled = false;
        await mw.InvokeAsync(ctx, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue();
    }

    // 3. Exactly at quota limit → calls next successfully (used + upload == limit is OK)
    [Fact]
    public async Task ExactlyAtLimit_CallsNext()
    {
        var options = new QuotaOptions
        {
            Enabled = true,
            DefaultLimitBytes = 1000
        };
        var (mw, service) = Build(options);
        await service.RecordUploadAsync("default", 500);

        // 500 used + 500 upload = 1000 == limit (not strictly greater, so should pass)
        var ctx = MakeContext(contentLength: 500);
        var nextCalled = false;
        await mw.InvokeAsync(ctx, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue();
    }

    // 4. Over quota → throws StorageValidationException
    [Fact]
    public async Task OverQuota_ThrowsStorageValidationException()
    {
        var options = new QuotaOptions
        {
            Enabled = true,
            DefaultLimitBytes = 1000
        };
        var (mw, service) = Build(options);
        await service.RecordUploadAsync("default", 900);

        var ctx = MakeContext(contentLength: 200); // 900 + 200 = 1100 > 1000

        var act = async () => await mw.InvokeAsync(ctx, NoOpNext);

        await act.Should().ThrowAsync<StorageValidationException>();
    }

    // 5. RecordUploadAsync increases used bytes
    [Fact]
    public async Task RecordUpload_IncreasesUsedBytes()
    {
        var options = new QuotaOptions { Enabled = true };
        var service = new InMemoryStorageQuotaService(options);

        await service.RecordUploadAsync("tenant1", 500);
        await service.RecordUploadAsync("tenant1", 300);

        var used = await service.GetUsedBytesAsync("tenant1");
        used.Should().Be(800);
    }

    // 6. RecordDeleteAsync decreases used bytes
    [Fact]
    public async Task RecordDelete_DecreasesUsedBytes()
    {
        var options = new QuotaOptions { Enabled = true };
        var service = new InMemoryStorageQuotaService(options);

        await service.RecordUploadAsync("tenant1", 1000);
        await service.RecordDeleteAsync("tenant1", 400);

        var used = await service.GetUsedBytesAsync("tenant1");
        used.Should().Be(600);
    }

    // 7. RecordDeleteAsync never goes below 0
    [Fact]
    public async Task RecordDelete_NeverGoesBelowZero()
    {
        var options = new QuotaOptions { Enabled = true };
        var service = new InMemoryStorageQuotaService(options);

        await service.RecordUploadAsync("tenant1", 100);
        await service.RecordDeleteAsync("tenant1", 500); // delete more than used

        var used = await service.GetUsedBytesAsync("tenant1");
        used.Should().Be(0);
    }

    // 8. GetQuotaLimitAsync returns per-scope limit when configured
    [Fact]
    public async Task GetQuotaLimitAsync_ReturnsPerScopeLimitWhenConfigured()
    {
        var options = new QuotaOptions
        {
            Enabled = true,
            DefaultLimitBytes = 1_000_000,
            Limits = new Dictionary<string, long>
            {
                ["premium"] = 10_000_000,
                ["free"] = 100_000
            }
        };
        var service = new InMemoryStorageQuotaService(options);

        var premiumLimit = await service.GetQuotaLimitAsync("premium");
        var freeLimit = await service.GetQuotaLimitAsync("free");

        premiumLimit.Should().Be(10_000_000);
        freeLimit.Should().Be(100_000);
    }

    // 9. GetQuotaLimitAsync returns DefaultLimitBytes when no per-scope limit exists
    [Fact]
    public async Task GetQuotaLimitAsync_ReturnsDefaultWhenNoScopeSpecified()
    {
        var options = new QuotaOptions
        {
            Enabled = true,
            DefaultLimitBytes = 500_000
        };
        var service = new InMemoryStorageQuotaService(options);

        var limit = await service.GetQuotaLimitAsync("unknown-scope");

        limit.Should().Be(500_000);
    }

    // 10. Scope is resolved from BucketOverride when no ScopeResolver configured
    [Fact]
    public async Task Scope_ResolvedFromBucketOverride_WhenNoScopeResolver()
    {
        var options = new QuotaOptions
        {
            Enabled = true,
            Limits = new Dictionary<string, long>
            {
                ["my-bucket"] = 1000
            }
        };
        var (mw, service) = Build(options);
        await service.RecordUploadAsync("my-bucket", 900);

        // Upload 200 bytes to "my-bucket" scope → should exceed limit of 1000
        var ctx = MakeContext(contentLength: 200, bucketOverride: "my-bucket");

        var act = async () => await mw.InvokeAsync(ctx, NoOpNext);

        await act.Should().ThrowAsync<StorageValidationException>();
    }
}
