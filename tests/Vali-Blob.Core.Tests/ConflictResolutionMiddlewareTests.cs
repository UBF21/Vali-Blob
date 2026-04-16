using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Exceptions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Pipeline.Middlewares;
using ValiBlob.Testing;
using ValiBlob.Testing.Extensions;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class ConflictResolutionMiddlewareTests
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

    /// <summary>Stores a file directly so exists-checks will find it.</summary>
    private static async Task SeedFileAsync(InMemoryStorageProvider provider, string path)
    {
        await provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(new byte[] { 0x00 }),
            ContentLength = 1
        });
    }

    private static StoragePipelineContext MakeContext(
        string path,
        ConflictResolution resolution = ConflictResolution.Overwrite)
    {
        var request = new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(new byte[] { 1, 2, 3 }),
            ContentLength = 3,
            ConflictResolution = resolution
        };
        return new StoragePipelineContext(request);
    }

    // 1. Overwrite → calls next even if file exists
    [Fact]
    public async Task Overwrite_CallsNextEvenIfFileExists()
    {
        var provider = BuildProvider();
        await SeedFileAsync(provider, "uploads/existing.txt");

        var mw = new ConflictResolutionMiddleware(provider);
        var ctx = MakeContext("uploads/existing.txt", ConflictResolution.Overwrite);

        var nextCalled = false;
        await mw.InvokeAsync(ctx, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue();
        ctx.IsCancelled.Should().BeFalse();
    }

    // 2. Fail → throws StorageValidationException when file exists
    [Fact]
    public async Task Fail_ThrowsStorageValidationExceptionWhenFileExists()
    {
        var provider = BuildProvider();
        await SeedFileAsync(provider, "uploads/exists.txt");

        var mw = new ConflictResolutionMiddleware(provider);
        var ctx = MakeContext("uploads/exists.txt", ConflictResolution.Fail);

        var act = async () => await mw.InvokeAsync(ctx, NoOpNext);

        await act.Should().ThrowAsync<StorageValidationException>();
    }

    // 3. Fail → calls next when file does NOT exist
    [Fact]
    public async Task Fail_CallsNextWhenFileDoesNotExist()
    {
        var provider = BuildProvider();
        // No seed — file does not exist

        var mw = new ConflictResolutionMiddleware(provider);
        var ctx = MakeContext("uploads/new-file.txt", ConflictResolution.Fail);

        var nextCalled = false;
        await mw.InvokeAsync(ctx, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue();
        ctx.IsCancelled.Should().BeFalse();
    }

    // 4. Rename → renames to _1 suffix when original exists
    [Fact]
    public async Task Rename_RenamesTo_1_SuffixWhenOriginalExists()
    {
        var provider = BuildProvider();
        await SeedFileAsync(provider, "uploads/photo.txt");

        var mw = new ConflictResolutionMiddleware(provider);
        var ctx = MakeContext("uploads/photo.txt", ConflictResolution.Rename);

        await mw.InvokeAsync(ctx, NoOpNext);

        ctx.Request.Path.ToString().Should().Be("uploads/photo_1.txt");
    }

    // 5. Rename → renames to _2 when _1 also exists
    [Fact]
    public async Task Rename_RenamesTo_2_WhenBothOriginalAnd_1_Exist()
    {
        var provider = BuildProvider();
        await SeedFileAsync(provider, "uploads/doc.txt");
        await SeedFileAsync(provider, "uploads/doc_1.txt");

        var mw = new ConflictResolutionMiddleware(provider);
        var ctx = MakeContext("uploads/doc.txt", ConflictResolution.Rename);

        await mw.InvokeAsync(ctx, NoOpNext);

        ctx.Request.Path.ToString().Should().Be("uploads/doc_2.txt");
    }

    // 6. Rename → preserves file extension in renamed path
    [Fact]
    public async Task Rename_PreservesFileExtension()
    {
        var provider = BuildProvider();
        await SeedFileAsync(provider, "uploads/archive.tar.gz");

        var mw = new ConflictResolutionMiddleware(provider);
        var ctx = MakeContext("uploads/archive.tar.gz", ConflictResolution.Rename);

        await mw.InvokeAsync(ctx, NoOpNext);

        // Extension .gz should be preserved
        ctx.Request.Path.ToString().Should().EndWith(".gz");
        ctx.Request.Path.ToString().Should().Contain("_1");
    }

    // 7. Rename → file does not exist → uses original path (no rename)
    [Fact]
    public async Task Rename_FileDoesNotExist_UsesOriginalPath()
    {
        var provider = BuildProvider();
        // No seed

        var mw = new ConflictResolutionMiddleware(provider);
        var ctx = MakeContext("uploads/brand-new.txt", ConflictResolution.Rename);

        await mw.InvokeAsync(ctx, NoOpNext);

        ctx.Request.Path.ToString().Should().Be("uploads/brand-new.txt");
    }

    // 8. Rename → result path stored in context.Request.Path
    [Fact]
    public async Task Rename_ResultPathStoredInContextRequest()
    {
        var provider = BuildProvider();
        await SeedFileAsync(provider, "uploads/report.pdf");

        var mw = new ConflictResolutionMiddleware(provider);
        var ctx = MakeContext("uploads/report.pdf", ConflictResolution.Rename);

        StoragePath? pathAtNext = null;
        await mw.InvokeAsync(ctx, c =>
        {
            pathAtNext = c.Request.Path;
            return Task.CompletedTask;
        });

        pathAtNext.Should().NotBeNull();
        pathAtNext!.ToString().Should().Be("uploads/report_1.pdf");
        // Context should also reflect the same value
        ctx.Request.Path.ToString().Should().Be("uploads/report_1.pdf");
    }

    // 9. Default ConflictResolution on new UploadRequest is Overwrite
    [Fact]
    public void NewUploadRequest_DefaultConflictResolutionIsOverwrite()
    {
        var request = new UploadRequest
        {
            Path = StoragePath.From("uploads/file.txt"),
            Content = new MemoryStream(new byte[] { 1 })
        };

        request.ConflictResolution.Should().Be(ConflictResolution.Overwrite);
    }
}
