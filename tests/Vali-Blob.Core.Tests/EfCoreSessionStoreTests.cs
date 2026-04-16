using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.EFCore;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class EfCoreSessionStoreTests
{
    private static (ValiResumableDbContext context, EfCoreResumableSessionStore store) CreateStore()
    {
        var options = new DbContextOptionsBuilder<ValiResumableDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new ValiResumableDbContext(options);
        var store   = new EfCoreResumableSessionStore(context, NullLogger<EfCoreResumableSessionStore>.Instance);
        return (context, store);
    }

    private static ResumableUploadSession MakeSession(string id) => new()
    {
        UploadId    = id,
        Path        = $"uploads/{id}/file.bin",
        TotalSize   = 4096,
        BytesUploaded = 0,
        ContentType = "application/octet-stream",
        ExpiresAt   = DateTimeOffset.UtcNow.AddHours(24)
    };

    // ─── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_Then_GetAsync_ReturnsSameSession()
    {
        var (_, store) = CreateStore();
        var session = MakeSession("ef-save-get");

        await store.SaveAsync(session);
        var result = await store.GetAsync("ef-save-get");

        result.Should().NotBeNull();
        result!.UploadId.Should().Be("ef-save-get");
    }

    [Fact]
    public async Task SaveAsync_GetAsync_ReturnsCorrectPathTotalSizeContentType()
    {
        var (_, store) = CreateStore();
        var session = new ResumableUploadSession
        {
            UploadId    = "ef-fields",
            Path        = "bucket/2024/video.mp4",
            TotalSize   = 1_048_576,
            BytesUploaded = 0,
            ContentType = "video/mp4",
            ExpiresAt   = DateTimeOffset.UtcNow.AddHours(1)
        };

        await store.SaveAsync(session);
        var result = await store.GetAsync("ef-fields");

        result.Should().NotBeNull();
        result!.Path.Should().Be("bucket/2024/video.mp4");
        result.TotalSize.Should().Be(1_048_576);
        result.ContentType.Should().Be("video/mp4");
    }

    [Fact]
    public async Task UpdateAsync_ChangesBytesUploaded_GetAsyncReturnsUpdatedValue()
    {
        var (_, store) = CreateStore();
        var session = MakeSession("ef-update");
        await store.SaveAsync(session);

        session.BytesUploaded = 2048;
        await store.UpdateAsync(session);

        var result = await store.GetAsync("ef-update");
        result.Should().NotBeNull();
        result!.BytesUploaded.Should().Be(2048);
    }

    [Fact]
    public async Task DeleteAsync_GetAsyncReturnsNull()
    {
        var (_, store) = CreateStore();
        var session = MakeSession("ef-delete");
        await store.SaveAsync(session);

        await store.DeleteAsync("ef-delete");
        var result = await store.GetAsync("ef-delete");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_UnknownUploadId_ReturnsNull()
    {
        var (_, store) = CreateStore();

        var result = await store.GetAsync("does-not-exist");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_WithMetadata_GetAsyncReturnsSameMetadata()
    {
        var (_, store) = CreateStore();
        var session = MakeSession("ef-metadata");
        session.Metadata = new Dictionary<string, string>
        {
            ["author"]   = "Alice",
            ["category"] = "documents"
        };

        await store.SaveAsync(session);
        var result = await store.GetAsync("ef-metadata");

        result.Should().NotBeNull();
        result!.Metadata.Should().NotBeNull();
        result.Metadata!["author"].Should().Be("Alice");
        result.Metadata["category"].Should().Be("documents");
    }

    [Fact]
    public async Task SaveAsync_WithProviderData_GetAsyncReturnsSameProviderData()
    {
        var (_, store) = CreateStore();
        var session = MakeSession("ef-providerdata");
        session.ProviderData["uploadUrl"]  = "https://s3.example.com/multipart/123";
        session.ProviderData["uploadId"]   = "mpu-abc-123";

        await store.SaveAsync(session);
        var result = await store.GetAsync("ef-providerdata");

        result.Should().NotBeNull();
        result!.ProviderData["uploadUrl"].Should().Be("https://s3.example.com/multipart/123");
        result.ProviderData["uploadId"].Should().Be("mpu-abc-123");
    }

    [Fact]
    public async Task MultipleSessions_EachRetrievableByOwnUploadId()
    {
        var (_, store) = CreateStore();
        var ids = new[] { "ef-multi-1", "ef-multi-2", "ef-multi-3" };

        foreach (var id in ids)
            await store.SaveAsync(MakeSession(id));

        foreach (var id in ids)
        {
            var result = await store.GetAsync(id);
            result.Should().NotBeNull($"session '{id}' should be retrievable");
            result!.UploadId.Should().Be(id);
        }
    }
}
