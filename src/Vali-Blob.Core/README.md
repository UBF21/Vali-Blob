# Vali-Blob.Core

[![NuGet](https://img.shields.io/nuget/v/ValiBlob.Core.svg)](https://www.nuget.org/packages/ValiBlob.Core)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](https://github.com/UBF21/Vali-Blob/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6%20%7C%207%20%7C%208%20%7C%209-purple)](https://www.nuget.org/packages/ValiBlob.Core)

Core abstractions, middleware pipeline, and shared infrastructure for the **Vali-Blob** ecosystem.

This package defines the `IStorageProvider` interface and all contracts used across every Vali-Blob provider. It is **required** by every Vali-Blob application regardless of which cloud provider you target.

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
```

Then add the provider package of your choice:

```bash
dotnet add package ValiBlob.AWS       # Amazon S3 / MinIO
dotnet add package ValiBlob.Azure     # Azure Blob Storage
dotnet add package ValiBlob.GCP       # Google Cloud Storage
dotnet add package ValiBlob.OCI       # Oracle Cloud Infrastructure
dotnet add package ValiBlob.Supabase  # Supabase Storage
dotnet add package ValiBlob.Local     # Local filesystem (dev/test)
```

---

## Key abstractions

| Type | Description |
|---|---|
| `IStorageProvider` | Upload, download, delete, list, copy, move, exists |
| `IStorageFactory` | Resolve a named or default provider at runtime |
| `StoragePath` | Typed, normalised path — use instead of raw strings |
| `StorageResult<T>` | Discriminated result with `IsSuccess`, `Value`, `ErrorMessage` |
| `IResumableUploadProvider` | Chunked upload with pause/resume support |
| `IPresignedUrlProvider` | Temporary signed upload/download URLs |
| `IResumableSessionStore` | Pluggable backend for resumable session tracking |

---

## Basic setup

### `appsettings.json`

```json
{
  "ValiBlob": {
    "DefaultProvider": "AWS"
  }
}
```

### `Program.cs`

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.AWS.Extensions;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseAWS();
```

---

## Upload a file

```csharp
public class FileService(IStorageProvider storage)
{
    public async Task<string> UploadAsync(Stream content, string fileName)
    {
        var result = await storage.UploadAsync(new UploadRequest
        {
            Path        = StoragePath.From("uploads", fileName),
            Content     = content,
            ContentType = "application/octet-stream"
        });

        if (!result.IsSuccess)
            throw new Exception(result.ErrorMessage);

        return result.Value!.Url;
    }
}
```

## Download a file

```csharp
var result = await storage.DownloadAsync(new DownloadRequest
{
    Path = StoragePath.From("uploads", "report.pdf")
});

if (result.IsSuccess)
{
    using var stream = result.Value!;
    // use stream
}
```

## Delete a file

```csharp
var result = await storage.DeleteAsync("uploads/report.pdf");
if (!result.IsSuccess)
    Console.WriteLine($"Delete failed: {result.ErrorMessage}");
```

---

## Middleware pipeline

Chain middleware in declaration order — each step wraps the next:

```csharp
builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseAWS()
    .WithPipeline(p => p
        .UseValidation(v =>
        {
            v.AllowedExtensions = new[] { ".jpg", ".png", ".pdf" };
            v.MaxFileSizeBytes  = 10 * 1024 * 1024; // 10 MB
        })
        .UseCompression()              // GZip for text/JSON/XML
        .UseEncryption(e =>
        {
            e.Key = Convert.FromBase64String(builder.Configuration["Storage:EncryptionKey"]!);
        })
        .Use<MyAuditMiddleware>()      // custom middleware
    );
```

### Built-in middleware

| Middleware | Description |
|---|---|
| `UseValidation` | Extension allowlist/blocklist, max size, MIME filtering |
| `UseCompression` | GZip compression for text-based content types |
| `UseEncryption` | AES-256-CBC with per-file random IV |

---

## StoragePath

Use `StoragePath` instead of raw strings to avoid path separator bugs:

```csharp
var path = StoragePath.From("avatars", userId, "profile.jpg");
// → "avatars/{userId}/profile.jpg"

var path2 = StoragePath.From("2024", "01", "report.pdf");
// → "2024/01/report.pdf"
```

---

## StorageResult&lt;T&gt;

All operations return a `StorageResult<T>` — no exceptions for expected errors:

```csharp
var result = await storage.DownloadAsync(...);

if (result.IsSuccess)
    Console.WriteLine($"Size: {result.Value!.Length}");
else
    Console.WriteLine($"Error: {result.ErrorMessage}");
```

---

## Resilience (Polly)

Vali-Blob.Core ships a Polly-based retry pipeline out of the box:

```csharp
.AddValiBlob(opts =>
{
    opts.DefaultProvider = "AWS";
    opts.Retry.MaxAttempts    = 3;
    opts.Retry.BaseDelayMs    = 500;
    opts.CircuitBreaker.Enabled = true;
})
```

---

## Multi-provider

Register multiple providers and select at runtime:

```csharp
builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "Azure")
    .UseAWS()
    .UseAzure()
    .UseGCP();

// In a service
public class ArchiveService(IStorageFactory factory)
{
    public async Task MirrorAsync(Stream content, string path)
    {
        var aws   = factory.Create("AWS");
        var azure = factory.Create("Azure");

        await aws.UploadAsync(...);
        await azure.UploadAsync(...);
    }
}
```

---

## Documentation

Full documentation at [vali-blob-docs.netlify.app](https://vali-blob-docs.netlify.app)

- [Getting Started](https://vali-blob-docs.netlify.app/docs/quick-start)
- [Pipeline & Middleware](https://vali-blob-docs.netlify.app/docs/pipeline)
- [Resilience](https://vali-blob-docs.netlify.app/docs/resilience)
- [API Reference](https://vali-blob-docs.netlify.app/docs/api-reference)

---

## Links

- [GitHub Repository](https://github.com/UBF21/Vali-Blob)
- [NuGet Package](https://www.nuget.org/packages/ValiBlob.Core)

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
