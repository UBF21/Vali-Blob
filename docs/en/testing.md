# Testing

ValiBlob provides a first-class testing package that lets you write fast, reliable unit tests for storage-related code without any cloud credentials, network access, or Docker containers.

---

## Why `ValiBlob.Testing`

Testing code that interacts with cloud storage has historically been painful:

- Real providers require credentials, networking, and often incur costs
- Mocking `IStorageProvider` by hand with a framework like Moq is verbose and doesn't verify realistic behavior (e.g., that the content stream is actually read)
- Running cloud SDK emulators requires Docker and adds CI complexity

`ValiBlob.Testing` solves this with `InMemoryStorageProvider`: a complete, production-quality implementation of `IStorageProvider` backed by a `ConcurrentDictionary<string, StoredFile>`. It passes the same functional test suite as the real providers and includes dedicated test helper methods.

---

## Installation

Add the package to your **test project** only:

```bash
dotnet add package ValiBlob.Testing
```

---

## `InMemoryStorageProvider` reference

`InMemoryStorageProvider` extends `BaseStorageProvider` and implements every `IStorageProvider` method. It also exposes test helper members:

| Member | Type | Description |
|---|---|---|
| `ProviderName` | `string` | `"InMemory"` |
| `HasFile(path)` | `bool` | Returns `true` if a file at this exact path has been uploaded |
| `GetRawBytes(path)` | `byte[]` | Returns the raw bytes stored for the given path. Throws `FileNotFoundException` if not found |
| `AllPaths` | `IReadOnlyCollection<string>` | All paths currently in the store |
| `FileCount` | `int` | Number of files currently in the store |
| `Clear()` | `void` | Removes all files from the store |

### Provider behavior

| Operation | Behavior |
|---|---|
| `UploadAsync` | Reads stream into `byte[]`, stores under the path, reports 100% progress |
| `DownloadAsync` | Returns a new `MemoryStream` from stored bytes. Returns `FileNotFound` if missing |
| `DeleteAsync` | Removes the entry. Returns success even if the file did not exist |
| `ExistsAsync` | Returns `true`/`false` based on key presence |
| `GetUrlAsync` | Returns `inmemory://{path}` |
| `CopyAsync` | Copies the stored entry to a new key |
| `MoveAsync` | Copies then deletes the original |
| `GetMetadataAsync` | Returns size, content type, and custom metadata |
| `SetMetadataAsync` | Updates the metadata on the stored entry in-place |
| `ListFilesAsync` | Filters by prefix, respects `MaxResults` |
| `DeleteManyAsync` | Removes all specified paths |
| `ListAllAsync` | Yields all matching entries as `IAsyncEnumerable<FileEntry>` |
| `DeleteFolderAsync` | Removes all entries whose key starts with the prefix |
| `ListFoldersAsync` | Returns unique first-segment folder names under prefix |
| `UploadFromUrlAsync` | Returns `StorageErrorCode.NotSupported` |

---

## Full test setup example

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Testing;
using ValiBlob.Testing.Extensions;

public static class TestServiceProvider
{
    public static (ServiceProvider sp, InMemoryStorageProvider storage) Build()
    {
        var services = new ServiceCollection();

        // BindConfiguration requires IConfiguration — provide empty config for tests
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddLogging();

        services
            .AddValiBlob()
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var storage = sp.GetRequiredService<InMemoryStorageProvider>();

        return (sp, storage);
    }
}
```

---

## Example unit tests (xUnit + FluentAssertions)

```bash
dotnet add package xunit
dotnet add package FluentAssertions
dotnet add package ValiBlob.Testing
```

```csharp
using FluentAssertions;
using ValiBlob.Core.Models;
using ValiBlob.Testing;
using Xunit;

public sealed class FileUploadServiceTests
{
    private readonly InMemoryStorageProvider _storage;
    private readonly FileUploadService _sut;

    public FileUploadServiceTests()
    {
        var (_, storage) = TestServiceProvider.Build();
        _storage = storage;
        _sut = new FileUploadService(_storage);
    }

    [Fact]
    public async Task UploadAsync_ValidFile_StoresFileAndReturnsPath()
    {
        // Arrange
        var content = "Hello, ValiBlob!"u8.ToArray();
        var stream = new MemoryStream(content);

        // Act
        var path = await _sut.UploadDocumentAsync(stream, "hello.txt", "text/plain");

        // Assert
        path.Should().Be("documents/hello.txt");
        _storage.HasFile("documents/hello.txt").Should().BeTrue();
        _storage.GetRawBytes("documents/hello.txt").Should().Equal(content);
    }

    [Fact]
    public async Task DownloadAsync_ExistingFile_ReturnsCorrectContent()
    {
        // Arrange
        var content = "Document content"u8.ToArray();
        await _storage.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("docs", "test.txt"),
            Content = new MemoryStream(content),
            ContentType = "text/plain"
        });

        // Act
        var result = await _storage.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From("docs", "test.txt")
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        var downloaded = new MemoryStream();
        await result.Value!.CopyToAsync(downloaded);
        downloaded.ToArray().Should().Equal(content);
    }

    [Fact]
    public async Task DownloadAsync_MissingFile_ReturnsFileNotFound()
    {
        var result = await _storage.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From("nonexistent", "file.txt")
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(StorageErrorCode.FileNotFound);
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_RemovesFile()
    {
        // Arrange
        await _storage.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("files", "to-delete.pdf"),
            Content = new MemoryStream("x"u8.ToArray())
        });
        _storage.HasFile("files/to-delete.pdf").Should().BeTrue();

        // Act
        await _storage.DeleteAsync("files/to-delete.pdf");

        // Assert
        _storage.HasFile("files/to-delete.pdf").Should().BeFalse();
    }

    [Fact]
    public async Task ListFilesAsync_WithPrefix_ReturnsOnlyMatchingFiles()
    {
        // Arrange
        _storage.Clear();
        await _storage.UploadAsync(new UploadRequest { Path = StoragePath.From("docs", "a.pdf"), Content = new MemoryStream("x"u8.ToArray()) });
        await _storage.UploadAsync(new UploadRequest { Path = StoragePath.From("docs", "b.pdf"), Content = new MemoryStream("x"u8.ToArray()) });
        await _storage.UploadAsync(new UploadRequest { Path = StoragePath.From("images", "c.jpg"), Content = new MemoryStream("x"u8.ToArray()) });

        // Act
        var result = await _storage.ListFilesAsync("docs/");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().AllSatisfy(f => f.Path.Should().StartWith("docs/"));
    }

    [Fact]
    public async Task DeleteManyAsync_RemovesSpecifiedFiles_LeavesOthers()
    {
        // Arrange
        _storage.Clear();
        await _storage.UploadAsync(new UploadRequest { Path = StoragePath.From("batch", "a.txt"), Content = new MemoryStream("x"u8.ToArray()) });
        await _storage.UploadAsync(new UploadRequest { Path = StoragePath.From("batch", "b.txt"), Content = new MemoryStream("x"u8.ToArray()) });
        await _storage.UploadAsync(new UploadRequest { Path = StoragePath.From("batch", "c.txt"), Content = new MemoryStream("x"u8.ToArray()) });

        // Act
        var result = await _storage.DeleteManyAsync(new[]
        {
            StoragePath.From("batch", "a.txt"),
            StoragePath.From("batch", "b.txt")
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Deleted.Should().Be(2);
        result.Value.Failed.Should().Be(0);
        _storage.HasFile("batch/a.txt").Should().BeFalse();
        _storage.HasFile("batch/b.txt").Should().BeFalse();
        _storage.HasFile("batch/c.txt").Should().BeTrue();
    }

    [Fact]
    public async Task ListAllAsync_StreamsAllMatchingEntries()
    {
        // Arrange
        _storage.Clear();
        await _storage.UploadAsync(new UploadRequest { Path = StoragePath.From("stream", "a.txt"), Content = new MemoryStream("x"u8.ToArray()) });
        await _storage.UploadAsync(new UploadRequest { Path = StoragePath.From("stream", "b.txt"), Content = new MemoryStream("x"u8.ToArray()) });
        await _storage.UploadAsync(new UploadRequest { Path = StoragePath.From("other", "c.txt"), Content = new MemoryStream("x"u8.ToArray()) });

        // Act
        var entries = new List<FileEntry>();
        await foreach (var entry in _storage.ListAllAsync("stream/"))
            entries.Add(entry);

        // Assert
        entries.Should().HaveCount(2);
        entries.Should().AllSatisfy(e => e.Path.Should().StartWith("stream/"));
    }
}
```

---

## Integration tests with Testcontainers + MinIO

For integration tests that exercise the real AWS S3 provider against a locally running MinIO container, use the [Testcontainers.MsSql](https://dotnet.testcontainers.org/) (or `Testcontainers` base) to spin up MinIO.

### Requirements

- Docker Desktop or Docker Engine running locally
- `dotnet add package Testcontainers` (or `Testcontainers.Minio` if available)

### Example — MinIO integration test fixture

```bash
dotnet add package Testcontainers
```

```csharp
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.AWS.Extensions;
using Xunit;

public sealed class MinIOFixture : IAsyncLifetime
{
    private IContainer? _container;

    public string Endpoint { get; private set; } = "";
    public const string AccessKey = "minioadmin";
    public const string SecretKey = "minioadmin";
    public const string BucketName = "test-bucket";

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder()
            .WithImage("minio/minio:latest")
            .WithPortBinding(9000, true)
            .WithEnvironment("MINIO_ROOT_USER", AccessKey)
            .WithEnvironment("MINIO_ROOT_PASSWORD", SecretKey)
            .WithCommand("server", "/data")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(9000))
            .Build();

        await _container.StartAsync();

        Endpoint = $"http://localhost:{_container.GetMappedPublicPort(9000)}";
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

[Collection("MinIO")]
public sealed class MinIOIntegrationTests : IClassFixture<MinIOFixture>
{
    private readonly IStorageProvider _storage;

    public MinIOIntegrationTests(MinIOFixture fixture)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        services
            .AddValiBlob(o => o.DefaultProvider = "AWS")
            .UseMinIO(opts =>
            {
                opts.Bucket = MinIOFixture.BucketName;
                opts.ServiceUrl = fixture.Endpoint;
                opts.AccessKeyId = MinIOFixture.AccessKey;
                opts.SecretAccessKey = MinIOFixture.SecretKey;
            });

        var sp = services.BuildServiceProvider();
        _storage = sp.GetRequiredService<IStorageProvider>();
    }

    [Fact]
    public async Task Upload_ThenDownload_ReturnsOriginalContent()
    {
        var content = "Integration test content"u8.ToArray();

        var uploadResult = await _storage.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("integration", "test.txt"),
            Content = new MemoryStream(content),
            ContentType = "text/plain",
            ContentLength = content.Length
        });

        uploadResult.IsSuccess.Should().BeTrue();

        var downloadResult = await _storage.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From("integration", "test.txt")
        });

        downloadResult.IsSuccess.Should().BeTrue();

        var ms = new MemoryStream();
        await downloadResult.Value!.CopyToAsync(ms);
        ms.ToArray().Should().Equal(content);
    }
}
```

---

## When to use unit tests vs integration tests

| Scenario | Recommended approach |
|---|---|
| Testing service logic that calls `IStorageProvider` | Unit tests with `InMemoryStorageProvider` |
| Testing `StoragePath` construction and operators | Unit tests (no provider needed) |
| Testing pipeline middleware (validation, compression) | Unit tests with `InMemoryStorageProvider` |
| Testing event handlers | Unit tests with `InMemoryStorageProvider` |
| Verifying multipart upload behavior | Integration tests with MinIO |
| Verifying presigned URL generation | Integration tests with MinIO or real provider |
| CI smoke test — "can we reach the bucket?" | Integration tests (may require cloud credentials in CI) |
| Load / performance tests | Integration tests with MinIO or a dedicated cloud test account |

Unit tests with `InMemoryStorageProvider` are fast (milliseconds), require no infrastructure, and should form the bulk of your test suite. Integration tests should be reserved for provider-specific behavior that cannot be simulated in memory.
