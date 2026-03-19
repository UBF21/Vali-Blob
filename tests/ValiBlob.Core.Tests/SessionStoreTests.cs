using FluentAssertions;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Models;
using ValiBlob.Core.Resumable;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class SessionStoreTests
{
    private static InMemoryResumableSessionStore CreateStore()
    {
        var opts = Microsoft.Extensions.Options.Options.Create(new Core.Options.ResumableUploadOptions());
        return new InMemoryResumableSessionStore(opts);
    }

    [Fact]
    public async Task SaveAsync_ShouldPersistSession()
    {
        using var store = CreateStore();
        var session = new ResumableUploadSession
        {
            UploadId = "id-save",
            Path = "uploads/file.bin",
            TotalSize = 1024,
            BytesUploaded = 0,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        await store.SaveAsync(session);

        var retrieved = await store.GetAsync("id-save");
        retrieved.Should().NotBeNull();
        retrieved!.UploadId.Should().Be("id-save");
    }

    [Fact]
    public async Task GetAsync_ExistingSession_ShouldReturn()
    {
        using var store = CreateStore();
        var session = new ResumableUploadSession
        {
            UploadId = "id-get",
            Path = "uploads/file.bin",
            TotalSize = 2048,
            BytesUploaded = 512,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(2)
        };

        await store.SaveAsync(session);

        var retrieved = await store.GetAsync("id-get");

        retrieved.Should().NotBeNull();
        retrieved!.Path.Should().Be("uploads/file.bin");
        retrieved.TotalSize.Should().Be(2048);
        retrieved.BytesUploaded.Should().Be(512);
    }

    [Fact]
    public async Task GetAsync_NonExistentSession_ShouldReturnNull()
    {
        using var store = CreateStore();

        var result = await store.GetAsync("nonexistent-id");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ExpiredSession_ShouldReturnNull()
    {
        using var store = CreateStore();
        var session = new ResumableUploadSession
        {
            UploadId = "id-expired",
            Path = "uploads/expired.bin",
            TotalSize = 1024,
            BytesUploaded = 0,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) // already expired
        };

        await store.SaveAsync(session);

        var result = await store.GetAsync("id-expired");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_NotExpiredSession_ShouldReturn()
    {
        using var store = CreateStore();
        var session = new ResumableUploadSession
        {
            UploadId = "id-valid",
            Path = "uploads/valid.bin",
            TotalSize = 1024,
            BytesUploaded = 0,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(10) // far future
        };

        await store.SaveAsync(session);

        var result = await store.GetAsync("id-valid");

        result.Should().NotBeNull();
        result!.UploadId.Should().Be("id-valid");
    }

    [Fact]
    public async Task UpdateAsync_ShouldModifySession()
    {
        using var store = CreateStore();
        var session = new ResumableUploadSession
        {
            UploadId = "id-update",
            Path = "uploads/update.bin",
            TotalSize = 4096,
            BytesUploaded = 0,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        await store.SaveAsync(session);

        session.BytesUploaded = 1024;
        await store.UpdateAsync(session);

        var updated = await store.GetAsync("id-update");
        updated.Should().NotBeNull();
        updated!.BytesUploaded.Should().Be(1024);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveSession()
    {
        using var store = CreateStore();
        var session = new ResumableUploadSession
        {
            UploadId = "id-delete",
            Path = "uploads/todelete.bin",
            TotalSize = 512,
            BytesUploaded = 0,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        await store.SaveAsync(session);
        await store.DeleteAsync("id-delete");

        var result = await store.GetAsync("id-delete");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_MultipleSessions_AllPersisted()
    {
        using var store = CreateStore();

        for (var i = 1; i <= 5; i++)
        {
            await store.SaveAsync(new ResumableUploadSession
            {
                UploadId = $"id-multi-{i}",
                Path = $"uploads/file{i}.bin",
                TotalSize = i * 1024,
                BytesUploaded = 0,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
        }

        for (var i = 1; i <= 5; i++)
        {
            var retrieved = await store.GetAsync($"id-multi-{i}");
            retrieved.Should().NotBeNull($"session id-multi-{i} should exist");
            retrieved!.TotalSize.Should().Be(i * 1024);
        }
    }
}
