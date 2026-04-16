using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Migration;
using ValiBlob.Core.Models;
using ValiBlob.Testing;
using ValiBlob.Testing.Extensions;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class StorageMigratorTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static (InMemoryStorageProvider source, InMemoryStorageProvider dest, StorageMigrator migrator)
        CreateMigratorWithProviders(string sourceName = "Source", string destName = "Dest")
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddValiBlob().UseInMemory();
        var sp = services.BuildServiceProvider();

        // Build two separate InMemoryStorageProvider instances via DI (each has its own isolated store)
        var source = sp.GetRequiredService<InMemoryStorageProvider>();
        var dest   = ActivatorUtilities.CreateInstance<InMemoryStorageProvider>(sp);

        var factory = new FakeStorageFactory(new Dictionary<string, IStorageProvider>(StringComparer.Ordinal)
        {
            [sourceName] = source,
            [destName]   = dest
        });

        var migrator = new StorageMigrator(factory, NullLogger<StorageMigrator>.Instance);
        return (source, dest, migrator);
    }

    private static async Task SeedAsync(InMemoryStorageProvider provider, string path, string content = "hello")
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        await provider.UploadAsync(new UploadRequest
        {
            Path    = StoragePath.From(path),
            Content = new MemoryStream(bytes),
            ContentType  = "text/plain",
            ContentLength = bytes.Length
        });
    }

    // ─── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task MigrateAsync_SingleFile_AppearsInDestination()
    {
        var (source, dest, migrator) = CreateMigratorWithProviders();
        await SeedAsync(source, "file.txt");

        await migrator.MigrateAsync("Source", "Dest");

        dest.HasFile("file.txt").Should().BeTrue();
    }

    [Fact]
    public async Task MigrateAsync_SingleFile_ContentIsIdenticalInDestination()
    {
        var (source, dest, migrator) = CreateMigratorWithProviders();
        await SeedAsync(source, "data.bin", "exact-content");

        await migrator.MigrateAsync("Source", "Dest");

        var bytes = dest.GetRawBytes("data.bin");
        System.Text.Encoding.UTF8.GetString(bytes).Should().Be("exact-content");
    }

    [Fact]
    public async Task MigrateAsync_SkipExisting_True_DoesNotOverwriteDestination()
    {
        var (source, dest, migrator) = CreateMigratorWithProviders();
        await SeedAsync(source, "file.txt", "source-content");
        await SeedAsync(dest, "file.txt", "dest-content");

        var result = await migrator.MigrateAsync("Source", "Dest",
            new MigrationOptions { SkipExisting = true });

        result.Skipped.Should().Be(1);
        // Destination content should remain unchanged
        var bytes = dest.GetRawBytes("file.txt");
        System.Text.Encoding.UTF8.GetString(bytes).Should().Be("dest-content");
    }

    [Fact]
    public async Task MigrateAsync_SkipExisting_False_OverwritesDestination()
    {
        var (source, dest, migrator) = CreateMigratorWithProviders();
        await SeedAsync(source, "file.txt", "new-content");
        await SeedAsync(dest, "file.txt", "old-content");

        var result = await migrator.MigrateAsync("Source", "Dest",
            new MigrationOptions { SkipExisting = false });

        result.Migrated.Should().Be(1);
        var bytes = dest.GetRawBytes("file.txt");
        System.Text.Encoding.UTF8.GetString(bytes).Should().Be("new-content");
    }

    [Fact]
    public async Task MigrateAsync_DryRun_True_NoFilesAppearInDestination()
    {
        var (source, dest, migrator) = CreateMigratorWithProviders();
        await SeedAsync(source, "file1.txt");
        await SeedAsync(source, "file2.txt");

        var result = await migrator.MigrateAsync("Source", "Dest",
            new MigrationOptions { DryRun = true });

        result.Migrated.Should().BeGreaterThan(0);
        dest.FileCount.Should().Be(0);
    }

    [Fact]
    public async Task MigrateAsync_DeleteSourceAfterCopy_True_RemovesFromSource()
    {
        var (source, dest, migrator) = CreateMigratorWithProviders();
        await SeedAsync(source, "removeme.txt");

        await migrator.MigrateAsync("Source", "Dest",
            new MigrationOptions { DeleteSourceAfterCopy = true });

        source.HasFile("removeme.txt").Should().BeFalse();
    }

    [Fact]
    public async Task MigrateAsync_DeleteSourceAfterCopy_False_LeavesSourceIntact()
    {
        var (source, dest, migrator) = CreateMigratorWithProviders();
        await SeedAsync(source, "keepme.txt");

        await migrator.MigrateAsync("Source", "Dest",
            new MigrationOptions { DeleteSourceAfterCopy = false });

        source.HasFile("keepme.txt").Should().BeTrue();
    }

    [Fact]
    public async Task MigrateAsync_MultipleFiles_AllMigrated()
    {
        var (source, dest, migrator) = CreateMigratorWithProviders();
        for (var i = 1; i <= 5; i++)
            await SeedAsync(source, $"file{i}.txt", $"content{i}");

        var result = await migrator.MigrateAsync("Source", "Dest");

        result.Migrated.Should().Be(5);
        dest.FileCount.Should().Be(5);
    }

    [Fact]
    public async Task MigrateAsync_PrefixFilter_OnlyMatchingFilesMigrated()
    {
        var (source, dest, migrator) = CreateMigratorWithProviders();
        await SeedAsync(source, "images/photo.jpg");
        await SeedAsync(source, "images/banner.png");
        await SeedAsync(source, "docs/report.pdf");

        var result = await migrator.MigrateAsync("Source", "Dest",
            new MigrationOptions { Prefix = "images/" });

        result.Migrated.Should().Be(2);
        dest.HasFile("images/photo.jpg").Should().BeTrue();
        dest.HasFile("images/banner.png").Should().BeTrue();
        dest.HasFile("docs/report.pdf").Should().BeFalse();
    }

    [Fact]
    public async Task MigrateAsync_MaxFiles_LimitsCount()
    {
        var (source, dest, migrator) = CreateMigratorWithProviders();
        for (var i = 1; i <= 5; i++)
            await SeedAsync(source, $"file{i}.txt");

        var result = await migrator.MigrateAsync("Source", "Dest",
            new MigrationOptions { MaxFiles = 1 });

        result.Migrated.Should().Be(1);
        dest.FileCount.Should().Be(1);
    }

    [Fact]
    public async Task MigrateAsync_TotalBytesTransferred_GreaterThanZero()
    {
        var (source, dest, migrator) = CreateMigratorWithProviders();
        await SeedAsync(source, "data.txt", "some bytes here");

        var result = await migrator.MigrateAsync("Source", "Dest");

        result.TotalBytesTransferred.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MigrateAsync_UnknownSourceProvider_ThrowsInvalidOperationException()
    {
        var (_, _, migrator) = CreateMigratorWithProviders();

        var act = async () => await migrator.MigrateAsync("NonExistentSource", "Dest");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*NonExistentSource*");
    }

    [Fact]
    public async Task MigrateAsync_UnknownDestProvider_ThrowsInvalidOperationException()
    {
        var (_, _, migrator) = CreateMigratorWithProviders();

        var act = async () => await migrator.MigrateAsync("Source", "NonExistentDest");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*NonExistentDest*");
    }

    // ─── FakeStorageFactory ──────────────────────────────────────────────────

    private sealed class FakeStorageFactory : IStorageFactory
    {
        private readonly Dictionary<string, IStorageProvider> _providers;

        public FakeStorageFactory(Dictionary<string, IStorageProvider> providers)
            => _providers = providers;

        public IStorageProvider Create(string? providerName = null)
        {
            if (providerName is null || !_providers.TryGetValue(providerName, out var provider))
                return null!; // StorageMigrator will check for null and throw InvalidOperationException
            return provider;
        }

        public IStorageProvider Create<TProvider>() where TProvider : IStorageProvider
            => _providers.Values.OfType<TProvider>().FirstOrDefault()!;

        public IEnumerable<IStorageProvider> GetAll() => _providers.Values;
    }
}
