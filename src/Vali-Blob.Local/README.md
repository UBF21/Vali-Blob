# Vali-Blob.Local

[![NuGet](https://img.shields.io/nuget/v/ValiBlob.Local.svg)](https://www.nuget.org/packages/ValiBlob.Local)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](https://github.com/UBF21/Vali-Blob/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6%20%7C%207%20%7C%208%20%7C%209-purple)](https://www.nuget.org/packages/ValiBlob.Local)

Local filesystem storage provider for **Vali-Blob** — ideal for development, integration testing, Docker Compose, and offline scenarios.

Implements the full `IStorageProvider` interface backed by the local disk. It is a drop-in replacement for any cloud provider: change a single DI registration to switch from local storage to AWS S3, Azure Blob, or any other Vali-Blob provider without touching your application code.

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
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.Local
```

---

## Configuration

### `appsettings.json`

```json
{
  "ValiBlob": {
    "DefaultProvider": "Local"
  },
  "ValiBlob:Local": {
    "BasePath":                "/var/app/storage",
    "PublicBaseUrl":           "http://localhost:5000/files",
    "CreateIfNotExists":       true,
    "PreserveDirectoryStructure": true
  }
}
```

### Environment-based switching

```json
// appsettings.Development.json
{
  "ValiBlob": { "DefaultProvider": "Local" },
  "ValiBlob:Local": {
    "BasePath":      "./local-storage",
    "PublicBaseUrl": "http://localhost:5000/files"
  }
}

// appsettings.Production.json
{
  "ValiBlob": { "DefaultProvider": "AWS" }
}
```

---

## Registration

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Local.Extensions;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "Local")
    .UseLocal();
```

### With explicit options

```csharp
builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "Local")
    .UseLocal(opts =>
    {
        opts.BasePath      = Path.Combine(builder.Environment.ContentRootPath, "storage");
        opts.PublicBaseUrl = "http://localhost:5000/files";
        opts.CreateIfNotExists = true;
    });
```

### With pipeline

```csharp
builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "Local")
    .UseLocal()
    .WithPipeline(p => p
        .UseValidation(v =>
        {
            v.AllowedExtensions = new[] { ".jpg", ".png", ".pdf" };
            v.MaxFileSizeBytes  = 20 * 1024 * 1024; // 20 MB
        })
    );
```

---

## Usage

### Upload

```csharp
public class FileService(IStorageProvider storage)
{
    public async Task<string> UploadAsync(IFormFile file)
    {
        await using var stream = file.OpenReadStream();

        var result = await storage.UploadAsync(new UploadRequest
        {
            Path        = StoragePath.From("uploads", file.FileName),
            Content     = stream,
            ContentType = file.ContentType
        });

        if (!result.IsSuccess)
            throw new Exception(result.ErrorMessage);

        // Returns http://localhost:5000/files/uploads/file.jpg
        return result.Value!.Url;
    }
}
```

### Download

```csharp
var result = await storage.DownloadAsync(new DownloadRequest
{
    Path = StoragePath.From("uploads", "photo.jpg")
});

if (result.IsSuccess)
    await result.Value!.CopyToAsync(Response.Body);
```

### Delete

```csharp
await storage.DeleteAsync("uploads/old-photo.jpg");
```

### List

```csharp
var result = await storage.ListAsync(new ListRequest
{
    Prefix = "uploads/"
});

foreach (var item in result.Value!)
    Console.WriteLine($"{item.Path} — {item.SizeBytes} bytes");
```

### Serve files via ASP.NET Core static files

Combine the local provider with `UseStaticFiles` for zero-dependency local serving:

```csharp
// Program.cs
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider("/var/app/storage"),
    RequestPath  = "/files"
});
```

### Resumable upload

```csharp
var session = await resumable.StartUploadAsync(new ResumableUploadRequest
{
    FileName    = "large-file.zip",
    ContentType = "application/zip",
    TotalSize   = totalBytes
});

// Chunks are stored as temp files and assembled on CompleteUploadAsync
await resumable.UploadChunkAsync(new ResumableChunkRequest
{
    SessionId  = session.SessionId,
    ChunkIndex = 0,
    Data       = chunkStream
});

await resumable.CompleteUploadAsync(session.SessionId);
```

---

## Features

| Feature | Supported |
|---|---|
| Upload / Download / Delete / List | Yes |
| Exists check | Yes |
| Copy / Move | Yes |
| Resumable chunked uploads | Yes |
| Presigned URL stubs (local URLs) | Yes |
| Sidecar `.meta.json` metadata | Yes |
| Range download support | Yes |
| Path traversal protection | Yes |
| BucketOverride per request | Yes |
| Auto-create base directory | Yes (opt-in) |

---

## Options reference

| Property | Default | Description |
|---|---|---|
| `BasePath` | — | Root directory for all stored files (required) |
| `PublicBaseUrl` | `null` | Base URL returned by `GetUrlAsync`. Falls back to `file://` if not set |
| `CreateIfNotExists` | `true` | Auto-create `BasePath` on startup if it does not exist |
| `PreserveDirectoryStructure` | `true` | Mirror the path hierarchy inside `BasePath` |

---

## Docker Compose example

```yaml
services:
  api:
    image: my-api
    volumes:
      - ./local-storage:/var/app/storage
    environment:
      ValiBlob__DefaultProvider: Local
      ValiBlob__Local__BasePath: /var/app/storage
      ValiBlob__Local__PublicBaseUrl: http://localhost:5000/files
```

---

## Documentation

- [Local provider docs](https://vali-blob-docs.netlify.app/docs/providers/local)
- [Full documentation](https://vali-blob-docs.netlify.app)

---

## Links

- [GitHub Repository](https://github.com/UBF21/Vali-Blob)
- [NuGet Package](https://www.nuget.org/packages/ValiBlob.Local)

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
