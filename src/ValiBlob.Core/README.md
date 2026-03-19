# ValiBlob.Core

Core abstractions and pipeline engine for the ValiBlob cloud storage library.

This package provides the `IStorageProvider` interface, the middleware pipeline builder, and the shared models used across all ValiBlob provider packages. It is required by every ValiBlob application.

## Install

```bash
dotnet add package ValiBlob.Core
```

## Key abstractions

| Type | Description |
|---|---|
| `IStorageProvider` | Upload, download, delete, list, copy, move, exists |
| `IStorageFactory` | Resolve a named or default provider at runtime |
| `StoragePath` | Typed, normalised path — use instead of raw strings |
| `StorageResult<T>` | Discriminated result with `IsSuccess`, `Value`, `ErrorMessage` |
| `IResumableUploadProvider` | Chunked upload with pause/resume |
| `IPresignedUrlProvider` | Temporary signed upload/download URLs |

## Minimal setup

```csharp
// Program.cs
using ValiBlob.Core.DependencyInjection;
using ValiBlob.AWS.Extensions; // replace with your chosen provider

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseAWS()
    .WithPipeline(p => p
        .UseValidation(v =>
        {
            v.AllowedExtensions  = new[] { ".jpg", ".png", ".pdf" };
            v.MaxFileSizeBytes   = 10 * 1024 * 1024;
        }));
```

```csharp
// Usage
public class FileService(IStorageProvider storage)
{
    public async Task UploadAsync(Stream content, string name)
    {
        var result = await storage.UploadAsync(new UploadRequest
        {
            Path        = StoragePath.From("uploads", name),
            Content     = content,
            ContentType = "application/octet-stream"
        });

        if (!result.IsSuccess)
            throw new Exception(result.ErrorMessage);
    }
}
```

## Pipeline middleware

Chain middleware in declaration order — each step wraps the next:

```csharp
.WithPipeline(p => p
    .UseValidation(...)       // 1. reject invalid files
    .UseCompression(...)      // 2. gzip content
    .UseEncryption(...)       // 3. AES-256-CBC
    .Use<MyAuditMiddleware>() // 4. custom
)
```

## Documentation

Full documentation at [docs/en/README.md](../../docs/en/README.md).
