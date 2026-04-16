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

        var entries = new List<FileEntry>();
        await foreach (var entry in _provider.ListAllAsync("stream/"))
            entries.Add(entry);

        entries.Should().HaveCount(2);
        entries.Should().AllSatisfy(e => e.Path.Should().StartWith("stream/"));
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
}
