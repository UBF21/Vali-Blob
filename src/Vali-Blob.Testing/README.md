# Vali-Blob.Testing

[![NuGet](https://img.shields.io/nuget/v/ValiBlob.Testing.svg)](https://www.nuget.org/packages/ValiBlob.Testing)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](https://github.com/UBF21/Vali-Blob/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6%20%7C%207%20%7C%208%20%7C%209-purple)](https://www.nuget.org/packages/ValiBlob.Testing)

In-memory storage provider and testing utilities for **Vali-Blob** — the unified cloud storage abstraction library for .NET.

`InMemoryStorageProvider` implements the full `IStorageProvider` interface backed by a `ConcurrentDictionary`. No cloud credentials, no network calls, no infrastructure required. Drop-in replacement for any Vali-Blob provider — swap with a single DI registration change in your test setup.

> **Important:** Add this package only to **test projects**. Do not include it in production assemblies.

---

## Compatibility

| Target Framework | Supported |
|---|---|
| `netstandard2.0` | Yes |
| `netstandard2.1` | Yes |
| `net6.0` | Yes |
| `net7.0` | Yes |
| `net8.0` | Yes |
| `net9.0` | Yes |

---

## Installation

```bash
dotnet add package ValiBlob.Testing --project tests/MyApp.Tests
```

---

## Registration

### ASP.NET Core integration tests (`WebApplicationFactory`)

```csharp
public class StorageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public StorageTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace the real provider with InMemory
                services.AddValiBlob(opts => opts.DefaultProvider = "InMemory")
                        .UseInMemory();
            });
        });
    }
}
```

### Unit tests (manual instantiation)

```csharp
var inMemory = new InMemoryStorageProvider();
IStorageProvider storage = inMemory;

// Use storage in your service under test
var service = new FileService(storage);
```

---

## Usage

### Upload and verify

```csharp
[Fact]
public async Task UploadAsync_StoresFile_AndReturnsSuccess()
{
    var inMemory = new InMemoryStorageProvider();
    var content  = new MemoryStream("hello world"u8.ToArray());

    var result = await inMemory.UploadAsync(new UploadRequest
    {
        Path        = StoragePath.From("docs", "readme.txt"),
        Content     = content,
        ContentType = "text/plain"
    });

    Assert.True(result.IsSuccess);
    Assert.True(inMemory.HasFile("docs/readme.txt"));

    var bytes = inMemory.GetRawBytes("docs/readme.txt");
    Assert.Equal("hello world", Encoding.UTF8.GetString(bytes));
}
```

### Download

```csharp
[Fact]
public async Task DownloadAsync_ReturnsStoredContent()
{
    var inMemory = new InMemoryStorageProvider();

    await inMemory.UploadAsync(new UploadRequest
    {
        Path        = StoragePath.From("files", "data.json"),
        Content     = new MemoryStream("{}"u8.ToArray()),
        ContentType = "application/json"
    });

    var result = await inMemory.DownloadAsync(new DownloadRequest
    {
        Path = StoragePath.From("files", "data.json")
    });

    Assert.True(result.IsSuccess);
    using var reader = new StreamReader(result.Value!);
    Assert.Equal("{}", await reader.ReadToEndAsync());
}
```

### Delete

```csharp
[Fact]
public async Task DeleteAsync_RemovesFile()
{
    var inMemory = new InMemoryStorageProvider();

    await inMemory.UploadAsync(new UploadRequest
    {
        Path        = StoragePath.From("temp", "file.txt"),
        Content     = new MemoryStream("data"u8.ToArray()),
        ContentType = "text/plain"
    });

    await inMemory.DeleteAsync("temp/file.txt");

    Assert.False(inMemory.HasFile("temp/file.txt"));
}
```

### Test a service that uses `IStorageProvider`

```csharp
public class AvatarServiceTests
{
    private readonly InMemoryStorageProvider _inMemory;
    private readonly AvatarService _sut;

    public AvatarServiceTests()
    {
        _inMemory = new InMemoryStorageProvider();
        _sut      = new AvatarService(_inMemory);
    }

    [Fact]
    public async Task UploadAvatar_SavesFileUnderUserFolder()
    {
        var image = new MemoryStream(new byte[1024]);

        await _sut.UploadAvatarAsync(image, userId: "user-42", extension: ".jpg");

        Assert.True(_inMemory.HasFile("avatars/user-42.jpg"));
    }

    [Fact]
    public async Task UploadAvatar_WhenStorageFails_ThrowsException()
    {
        _inMemory.SimulateFailure = true; // force all operations to fail

        await Assert.ThrowsAsync<Exception>(() =>
            _sut.UploadAvatarAsync(new MemoryStream(), "user-99", ".jpg"));
    }
}
```

---

## Test helper API

| Member | Description |
|---|---|
| `HasFile(path)` | Returns `true` if a file exists at the given path |
| `GetRawBytes(path)` | Returns the raw bytes stored at a path |
| `GetAllPaths()` | Returns all stored file paths |
| `Clear()` | Removes all stored files |
| `FileCount` | Total number of stored files |
| `SimulateFailure` | When `true`, all operations return a failure result |

---

## Features

| Feature | Supported |
|---|---|
| Upload / Download / Delete / List | Yes |
| Exists check | Yes |
| Copy / Move | Yes |
| Thread-safe (`ConcurrentDictionary`) | Yes |
| Failure simulation | Yes |
| No credentials or network required | Yes |
| Swap from any cloud provider with one line | Yes |

---

## Documentation

- [Testing guide](https://vali-blob-docs.netlify.app/docs/testing)
- [Full documentation](https://vali-blob-docs.netlify.app)

---

## Links

- [GitHub Repository](https://github.com/UBF21/Vali-Blob)
- [NuGet Package](https://www.nuget.org/packages/ValiBlob.Testing)

---

## Donations

If Vali-Blob is useful to you, consider supporting its development:

- **Latin America** — [MercadoPago](https://link.mercadopago.com.pe/felipermm)
- **International** — [PayPal](https://paypal.me/felipeRMM?country.x=PE&locale.x=es_XC)

---

## License

[MIT License](https://github.com/UBF21/Vali-Blob/blob/main/LICENSE)

## Contributions

Issues and pull requests are welcome on [GitHub](https://github.com/UBF21/Vali-Blob).
