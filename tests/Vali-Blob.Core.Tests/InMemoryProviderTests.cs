using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Models;
using ValiBlob.Testing;
using ValiBlob.Testing.Extensions;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class InMemoryProviderTests
{
    private readonly InMemoryStorageProvider _provider;

    public InMemoryProviderTests()
    {
        var services = new ServiceCollection();

        // BindConfiguration requires IConfiguration — provide empty config for tests
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddLogging();
        services.AddValiBlob().UseInMemory();

        var sp = services.BuildServiceProvider();
        _provider = sp.GetRequiredService<InMemoryStorageProvider>();
    }

    [Fact]
    public async Task UploadAsync_ShouldStoreFile()
    {
        var content = "Hello, ValiBlob!"u8.ToArray();
        var request = new UploadRequest
        {
            Path = StoragePath.From("test", "hello.txt"),
            Content = new MemoryStream(content),
            ContentType = "text/plain",
            ContentLength = content.Length
        };

        var result = await _provider.UploadAsync(request);

        result.IsSuccess.Should().BeTrue();
        _provider.HasFile("test/hello.txt").Should().BeTrue();
    }

    [Fact]
    public async Task DownloadAsync_WhenFileExists_ShouldReturnContent()
    {
        var content = "ValiBlob content"u8.ToArray();
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("test", "download.txt"),
            Content = new MemoryStream(content),
            ContentLength = content.Length
        });

        var result = await _provider.DownloadAsync(new DownloadRequest { Path = StoragePath.From("test", "download.txt") });

        result.IsSuccess.Should().BeTrue();
        var downloaded = new MemoryStream();
        await result.Value!.CopyToAsync(downloaded);
        downloaded.ToArray().Should().BeEquivalentTo(content);
    }

    [Fact]
    public async Task DownloadAsync_WhenFileNotExists_ShouldReturnFailure()
    {
        var result = await _provider.DownloadAsync(new DownloadRequest { Path = StoragePath.From("nonexistent.txt") });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(StorageErrorCode.FileNotFound);
    }

    [Fact]
    public async Task ExistsAsync_WhenFileUploaded_ShouldReturnTrue()
    {
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("test", "exists.txt"),
            Content = new MemoryStream("x"u8.ToArray())
        });

        var result = await _provider.ExistsAsync("test/exists.txt");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveFile()
    {
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("test", "delete.txt"),
            Content = new MemoryStream("x"u8.ToArray())
        });

        await _provider.DeleteAsync("test/delete.txt");

        _provider.HasFile("test/delete.txt").Should().BeFalse();
    }

    [Fact]
    public async Task ListFilesAsync_WithPrefix_ShouldFilterCorrectly()
    {
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("docs", "a.pdf"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("docs", "b.pdf"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("images", "c.jpg"), Content = new MemoryStream("x"u8.ToArray()) });

        var result = await _provider.ListFilesAsync("docs/");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().AllSatisfy(f => f.Path.Should().StartWith("docs/"));
    }

    [Fact]
    public async Task CopyAsync_ShouldDuplicateFile()
    {
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("original.txt"),
            Content = new MemoryStream("content"u8.ToArray())
        });

        await _provider.CopyAsync("original.txt", "copy.txt");

        _provider.HasFile("original.txt").Should().BeTrue();
        _provider.HasFile("copy.txt").Should().BeTrue();
    }

    [Fact]
    public async Task DeleteManyAsync_ShouldRemoveAllSpecifiedFiles()
    {
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("batch", "a.txt"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("batch", "b.txt"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("batch", "c.txt"), Content = new MemoryStream("x"u8.ToArray()) });

        var paths = new[]
        {
            StoragePath.From("batch", "a.txt"),
            StoragePath.From("batch", "b.txt")
        };

        var result = await _provider.DeleteManyAsync(paths);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalRequested.Should().Be(2);
        result.Value.Deleted.Should().Be(2);
        result.Value.Failed.Should().Be(0);
        _provider.HasFile("batch/a.txt").Should().BeFalse();
        _provider.HasFile("batch/b.txt").Should().BeFalse();
        _provider.HasFile("batch/c.txt").Should().BeTrue();
    }

    [Fact]
    public async Task DeleteFolderAsync_ShouldRemoveAllFilesWithPrefix()
    {
        _provider.Clear();
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("folder", "a.txt"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("folder", "b.txt"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("other", "c.txt"), Content = new MemoryStream("x"u8.ToArray()) });

        var result = await _provider.DeleteFolderAsync("folder/");

        result.IsSuccess.Should().BeTrue();
        _provider.HasFile("folder/a.txt").Should().BeFalse();
        _provider.HasFile("folder/b.txt").Should().BeFalse();
        _provider.HasFile("other/c.txt").Should().BeTrue();
    }

    [Fact]
    public async Task ListFoldersAsync_ShouldReturnUniqueVirtualFolders()
    {
        _provider.Clear();
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("docs", "2024", "invoice.pdf"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("docs", "2023", "invoice.pdf"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("images", "photo.jpg"), Content = new MemoryStream("x"u8.ToArray()) });

        var result = await _provider.ListFoldersAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("docs");
        result.Value.Should().Contain("images");
    }

    [Fact]
    public async Task ListAllAsync_ShouldYieldAllMatchingEntries()
    {
        _provider.Clear();
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("stream", "a.txt"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("stream", "b.txt"), Content = new MemoryStream("x"u8.ToArray()) });
        await _provider.UploadAsync(new UploadRequest { Path = StoragePath.From("other", "c.txt"), Content = new MemoryStream("x"u8.ToArray()) });

        var count = 0;
        await foreach (var _ in _provider.ListAllAsync("stream/"))
            count++;

        count.Should().Be(2);
    }

    [Fact]
    public void StoragePath_From_ShouldJoinSegmentsWithSlash()
    {
        var path = StoragePath.From("documents", "invoices", "2024", "file.pdf");
        path.ToString().Should().Be("documents/invoices/2024/file.pdf");
    }

    [Fact]
    public void StoragePath_FileName_ShouldReturnLastSegment()
    {
        var path = StoragePath.From("documents", "invoices", "file.pdf");
        path.FileName.Should().Be("file.pdf");
    }

    [Fact]
    public void StoragePath_Extension_ShouldReturnDotExtension()
    {
        var path = StoragePath.From("documents", "file.pdf");
        path.Extension.Should().Be(".pdf");
    }

    [Fact]
    public void StoragePath_Parent_ShouldReturnPathWithoutLastSegment()
    {
        var path = StoragePath.From("documents", "invoices", "file.pdf");
        path.Parent!.ToString().Should().Be("documents/invoices");
    }

    [Fact]
    public void StoragePath_SlashOperator_ShouldAppendSegment()
    {
        var path = StoragePath.From("documents") / "invoices" / "file.pdf";
        path.ToString().Should().Be("documents/invoices/file.pdf");
    }

    [Fact]
    public void StoragePath_ImplicitFromString_ShouldSplitOnSlash()
    {
        StoragePath path = "documents/invoices/file.pdf";
        path.Segments.Count.Should().Be(3);
        path.ToString().Should().Be("documents/invoices/file.pdf");
    }

    [Fact]
    public void StoragePath_Equality_ShouldWorkCorrectly()
    {
        var a = StoragePath.From("docs", "file.pdf");
        var b = StoragePath.From("docs", "file.pdf");
        var c = StoragePath.From("docs", "other.pdf");

        (a == b).Should().BeTrue();
        (a != c).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void StoragePath_ImplicitToString_ShouldWork()
    {
        var path = StoragePath.From("docs", "file.pdf");
        string asString = path;
        asString.Should().Be("docs/file.pdf");
    }

    // ─── Negative path tests ─────────────────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_WhenFileNotUploaded_ShouldReturnFalse()
    {
        var result = await _provider.ExistsAsync("nonexistent/file.txt");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_WhenFileNotExists_ShouldReturnSuccess()
    {
        var result = await _provider.DeleteAsync("ghost/file.txt");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task CopyAsync_WhenSourceNotExists_ShouldReturnFailure()
    {
        var result = await _provider.CopyAsync("does-not-exist.txt", "destination.txt");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ListFilesAsync_WhenNoFilesMatch_ShouldReturnEmptyList()
    {
        var result = await _provider.ListFilesAsync("no-match-prefix/");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ListFoldersAsync_WhenNoFilesMatch_ShouldReturnEmptyList()
    {
        var result = await _provider.ListFoldersAsync("zzz-nonexistent-prefix/");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteManyAsync_WhenSomeFilesNotExist_ShouldReturnSuccess()
    {
        await _provider.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("existing.txt"),
            Content = new MemoryStream("x"u8.ToArray())
        });

        var paths = new[] { StoragePath.From("existing.txt"), StoragePath.From("missing.txt") };
        var result = await _provider.DeleteManyAsync(paths);

        result.IsSuccess.Should().BeTrue();
        _provider.HasFile("existing.txt").Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_WithEmptyContent_ShouldStoreZeroByteFile()
    {
        var request = new UploadRequest
        {
            Path = StoragePath.From("empty.bin"),
            Content = new MemoryStream([]),
            ContentLength = 0
        };

        var result = await _provider.UploadAsync(request);

        result.IsSuccess.Should().BeTrue();
        _provider.HasFile("empty.bin").Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_OverwritingExistingFile_ShouldReplaceContent()
    {
        var path = StoragePath.From("overwrite.txt");

        await _provider.UploadAsync(new UploadRequest
        {
            Path = path,
            Content = new MemoryStream("original"u8.ToArray())
        });

        await _provider.UploadAsync(new UploadRequest
        {
            Path = path,
            Content = new MemoryStream("updated"u8.ToArray())
        });

        var download = await _provider.DownloadAsync(new DownloadRequest { Path = path });
        var buf = new MemoryStream();
        await download.Value!.CopyToAsync(buf);
        Encoding.UTF8.GetString(buf.ToArray()).Should().Be("updated");
    }

    [Fact]
    public async Task DeleteFolderAsync_WhenFolderNotExists_ShouldReturnSuccess()
    {
        var result = await _provider.DeleteFolderAsync("nonexistent-folder/");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ListAllAsync_WhenNoFilesMatch_ShouldYieldNothing()
    {
        var count = 0;
        await foreach (var _ in _provider.ListAllAsync("zzz-nonexistent-prefix/"))
            count++;

        count.Should().Be(0);
    }
    // ─── UploadFromUrl allowlist (via LocalStorageProvider — InMemory overrides UploadFromUrl) ──────

    [Fact]
    public async Task LocalProvider_UploadFromUrlAsync_WhenHostNotInAllowlist_ReturnsFailure()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.Configure<ValiBlob.Local.Options.LocalStorageOptions>(o => { o.BasePath = tempPath; });
            services.Configure<ValiBlob.Core.Options.ResilienceOptions>(_ => { });
            services.Configure<ValiBlob.Core.Options.EncryptionOptions>(_ => { });
            services.AddSingleton<ValiBlob.Core.Pipeline.StoragePipelineBuilder>();
            var sp = services.BuildServiceProvider();

            var localProvider = ActivatorUtilities.CreateInstance<ValiBlob.Local.LocalStorageProvider>(sp);
            localProvider.SetAllowedUploadHosts(["cdn.trusted.com"]);

            var result = await localProvider.UploadFromUrlAsync(
                "http://evil.attacker.com/malware.bin",
                StoragePath.From("dest.bin"));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("allowed list");
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public async Task LocalProvider_UploadFromUrlAsync_WhenHostInAllowlist_PassesValidationCheck()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.Configure<ValiBlob.Local.Options.LocalStorageOptions>(o => { o.BasePath = tempPath; });
            services.Configure<ValiBlob.Core.Options.ResilienceOptions>(_ => { });
            services.Configure<ValiBlob.Core.Options.EncryptionOptions>(_ => { });
            services.AddSingleton<ValiBlob.Core.Pipeline.StoragePipelineBuilder>();
            var sp = services.BuildServiceProvider();

            var localProvider = ActivatorUtilities.CreateInstance<ValiBlob.Local.LocalStorageProvider>(sp);
            localProvider.SetAllowedUploadHosts(["cdn.trusted.com"]);

            // HTTP will fail (no real server) but allowlist check passes — error is NOT about allowlist
            var result = await localProvider.UploadFromUrlAsync(
                "https://cdn.trusted.com/image.png",
                StoragePath.From("image.png"));

            result.ErrorMessage.Should().NotContain("allowed list");
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

}
