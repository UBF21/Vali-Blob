# ValiBlob.Testing

In-memory storage provider for unit and integration testing of ValiBlob-based applications.

`InMemoryStorageProvider` implements the full `IStorageProvider` interface backed by a `ConcurrentDictionary`. No cloud credentials, no network calls, no infrastructure required.

## Install

```bash
dotnet add package ValiBlob.Testing
```

Add only to test projects — do not include in production assemblies.

## Register

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Testing.Extensions;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "InMemory")
    .UseInMemory();
```

## Test helpers

| Method | Description |
|---|---|
| `GetRawBytes(path)` | Read the raw bytes stored at a path |
| `HasFile(path)` | Check whether a file exists |
| `Clear()` | Remove all stored files |
| `GetAllPaths()` | List every stored path |

## Example — xUnit

```csharp
public class FileServiceTests
{
    private readonly IStorageProvider _storage;
    private readonly InMemoryStorageProvider _inMemory;

    public FileServiceTests()
    {
        _inMemory = new InMemoryStorageProvider();
        _storage  = _inMemory;
    }

    [Fact]
    public async Task UploadAsync_StoresFile()
    {
        var content = new MemoryStream("hello world"u8.ToArray());

        var result = await _storage.UploadAsync(new UploadRequest
        {
            Path        = StoragePath.From("docs", "readme.txt"),
            Content     = content,
            ContentType = "text/plain"
        });

        Assert.True(result.IsSuccess);
        Assert.True(_inMemory.HasFile("docs/readme.txt"));

        var bytes = _inMemory.GetRawBytes("docs/readme.txt");
        Assert.Equal("hello world", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile()
    {
        // Seed a file
        await _storage.UploadAsync(new UploadRequest
        {
            Path        = StoragePath.From("temp", "file.txt"),
            Content     = new MemoryStream("data"u8.ToArray()),
            ContentType = "text/plain"
        });

        await _storage.DeleteAsync("temp/file.txt");

        Assert.False(_inMemory.HasFile("temp/file.txt"));
    }
}
```

## Documentation

[Testing docs](../../docs/en/testing.md)
