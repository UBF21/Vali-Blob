using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Pipeline.Middlewares;
using ValiBlob.Testing;
using ValiBlob.Testing.Extensions;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class DeduplicationMiddlewareTests
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

    private static StoragePipelineContext MakeContext(byte[] content, string path = "uploads/test.bin")
    {
        var request = new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(content),
            ContentLength = content.Length
        };
        return new StoragePipelineContext(request);
    }

    // 1. When Enabled = false → passes through without touching metadata
    [Fact]
    public async Task Disabled_PassesThroughWithoutTouchingMetadata()
    {
        var provider = BuildProvider();
        var options = new DeduplicationOptions { Enabled = false };
        var mw = new DeduplicationMiddleware(provider, options);
        var ctx = MakeContext(new byte[] { 1, 2, 3 });

        await mw.InvokeAsync(ctx, NoOpNext);

        ctx.Request.Metadata.Should().BeNull();
        ctx.IsCancelled.Should().BeFalse();
    }

    // 2. First upload of a file → sets x-content-hash in metadata and calls next
    [Fact]
    public async Task FirstUpload_SetsHashMetadataAndCallsNext()
    {
        var provider = BuildProvider();
        var options = new DeduplicationOptions { Enabled = true, CheckBeforeUpload = true };
        var mw = new DeduplicationMiddleware(provider, options);
        var ctx = MakeContext(new byte[] { 10, 20, 30 });

        var nextCalled = false;
        await mw.InvokeAsync(ctx, c =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        nextCalled.Should().BeTrue();
        ctx.Request.Metadata.Should().NotBeNull();
        ctx.Request.Metadata!.Should().ContainKey("x-content-hash");
        ctx.Request.Metadata!["x-content-hash"].Should().NotBeNullOrEmpty();
    }

    // 3. Same content uploaded twice → second upload is cancelled
    [Fact]
    public async Task SameContent_SecondUploadIsCancelled()
    {
        var provider = BuildProvider();
        var options = new DeduplicationOptions { Enabled = true, CheckBeforeUpload = true };
        var mw = new DeduplicationMiddleware(provider, options);

        var content = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };

        // First upload: run through middleware then actually store via provider
        var ctx1 = MakeContext(content, "uploads/file1.bin");
        await mw.InvokeAsync(ctx1, async c =>
        {
            // Upload so the file (with its hash metadata) is in the store
            await provider.UploadAsync(c.Request);
        });
        ctx1.IsCancelled.Should().BeFalse();

        // Second upload of same content
        var ctx2 = MakeContext(content, "uploads/file2.bin");
        await mw.InvokeAsync(ctx2, NoOpNext);

        ctx2.IsCancelled.Should().BeTrue();
    }

    // 4. Different content → both pass through (not duplicates)
    [Fact]
    public async Task DifferentContent_BothPassThrough()
    {
        var provider = BuildProvider();
        var options = new DeduplicationOptions { Enabled = true, CheckBeforeUpload = true };
        var mw = new DeduplicationMiddleware(provider, options);

        var content1 = new byte[] { 1, 2, 3, 4 };
        var content2 = new byte[] { 5, 6, 7, 8 };

        var ctx1 = MakeContext(content1, "uploads/a.bin");
        await mw.InvokeAsync(ctx1, async c => { await provider.UploadAsync(c.Request); });

        var ctx2 = MakeContext(content2, "uploads/b.bin");
        await mw.InvokeAsync(ctx2, async c => { await provider.UploadAsync(c.Request); });

        ctx1.IsCancelled.Should().BeFalse();
        ctx2.IsCancelled.Should().BeFalse();
    }

    // 5. context.Items["deduplication.contentHash"] is set after middleware runs
    [Fact]
    public async Task ContentHashItem_IsSetAfterMiddlewareRuns()
    {
        var provider = BuildProvider();
        var options = new DeduplicationOptions { Enabled = true };
        var mw = new DeduplicationMiddleware(provider, options);
        var ctx = MakeContext(new byte[] { 0x01, 0x02 });

        await mw.InvokeAsync(ctx, NoOpNext);

        ctx.Items.Should().ContainKey("deduplication.contentHash");
        ctx.Items["deduplication.contentHash"].Should().BeOfType<string>();
        ((string)ctx.Items["deduplication.contentHash"]).Should().NotBeNullOrEmpty();
    }

    // 6. context.Items["deduplication.isDuplicate"] is true when duplicate detected
    [Fact]
    public async Task IsDuplicateItem_IsTrueWhenDuplicateDetected()
    {
        var provider = BuildProvider();
        var options = new DeduplicationOptions { Enabled = true, CheckBeforeUpload = true };
        var mw = new DeduplicationMiddleware(provider, options);

        var content = new byte[] { 0x11, 0x22, 0x33 };

        // First upload
        var ctx1 = MakeContext(content, "uploads/original.bin");
        await mw.InvokeAsync(ctx1, async c => { await provider.UploadAsync(c.Request); });

        // Duplicate
        var ctx2 = MakeContext(content, "uploads/duplicate.bin");
        await mw.InvokeAsync(ctx2, NoOpNext);

        ctx2.Items.Should().ContainKey("deduplication.isDuplicate");
        ctx2.Items["deduplication.isDuplicate"].Should().Be(true);
    }

    // 7. When CheckBeforeUpload = false → hash stamped in metadata but no duplicate check
    [Fact]
    public async Task CheckBeforeUploadFalse_HashStampedButNoDuplicateCheck()
    {
        var provider = BuildProvider();
        var options = new DeduplicationOptions
        {
            Enabled = true,
            CheckBeforeUpload = false
        };
        var mw = new DeduplicationMiddleware(provider, options);

        var content = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        // First upload
        var ctx1 = MakeContext(content, "uploads/x.bin");
        await mw.InvokeAsync(ctx1, async c => { await provider.UploadAsync(c.Request); });

        // Second upload of same content — should NOT be cancelled because CheckBeforeUpload = false
        var ctx2 = MakeContext(content, "uploads/y.bin");
        var nextCalled = false;
        await mw.InvokeAsync(ctx2, c => { nextCalled = true; return Task.CompletedTask; });

        ctx2.IsCancelled.Should().BeFalse();
        nextCalled.Should().BeTrue();
        ctx2.Request.Metadata.Should().ContainKey("x-content-hash");
    }

    // 8. Stream is rewound after hash computation (still readable after middleware)
    [Fact]
    public async Task SeekableStream_IsRewoundAfterHashComputation()
    {
        var provider = BuildProvider();
        var options = new DeduplicationOptions { Enabled = true, CheckBeforeUpload = false };
        var mw = new DeduplicationMiddleware(provider, options);

        var content = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var ctx = MakeContext(content);

        byte[]? capturedBytes = null;
        await mw.InvokeAsync(ctx, c =>
        {
            using var ms = new MemoryStream();
            c.Request.Content.CopyTo(ms);
            capturedBytes = ms.ToArray();
            return Task.CompletedTask;
        });

        capturedBytes.Should().NotBeNull();
        capturedBytes.Should().BeEquivalentTo(content);
    }
}
