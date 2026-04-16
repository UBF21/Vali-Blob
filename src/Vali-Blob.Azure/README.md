# Vali-Blob.Azure

[![NuGet](https://img.shields.io/nuget/v/ValiBlob.Azure.svg)](https://www.nuget.org/packages/ValiBlob.Azure)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](https://github.com/UBF21/Vali-Blob/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6%20%7C%207%20%7C%208%20%7C%209-purple)](https://www.nuget.org/packages/ValiBlob.Azure)

Azure Blob Storage provider for **Vali-Blob** — the unified cloud storage abstraction library for .NET.

Implements `IStorageProvider` over Azure Blob Storage with SAS token presigned URLs, resumable block-blob uploads, optional automatic container creation, and seamless DI registration.

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
dotnet add package ValiBlob.Azure
```

---

## Configuration

### Using a connection string

```json
{
  "ValiBlob": {
    "DefaultProvider": "Azure"
  },
  "ValiBlob:Azure": {
    "Container":                  "my-files",
    "ConnectionString":           "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net",
    "CreateContainerIfNotExists": true
  }
}
```

### Using account name + key

```json
{
  "ValiBlob:Azure": {
    "Container":   "my-files",
    "AccountName": "mystorageaccount",
    "AccountKey":  ""
  }
}
```

> **Security:** Never commit `AccountKey` or `ConnectionString` to source control. Use Azure Key Vault, environment variables, or Managed Identity instead.

### Using Managed Identity (recommended for Azure-hosted apps)

```json
{
  "ValiBlob:Azure": {
    "Container":   "my-files",
    "AccountName": "mystorageaccount"
  }
}
```

When `AccountKey` is absent and no `ConnectionString` is set, the provider uses `DefaultAzureCredential` — which automatically picks up Managed Identity on App Service, AKS, and Azure VMs.

---

## Registration

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Azure.Extensions;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "Azure")
    .UseAzure();
```

### With pipeline

```csharp
builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "Azure")
    .UseAzure()
    .WithPipeline(p => p
        .UseValidation(v =>
        {
            v.AllowedExtensions = new[] { ".jpg", ".png", ".pdf", ".docx" };
            v.MaxFileSizeBytes  = 100 * 1024 * 1024; // 100 MB
        })
        .UseEncryption(e =>
        {
            e.Key = Convert.FromBase64String(builder.Configuration["Storage:Key"]!);
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
{
    using var stream = result.Value!;
    await stream.CopyToAsync(Response.Body);
}
```

### Generate SAS presigned URL

```csharp
// Download URL — valid for 30 minutes
var download = await storage.GetPresignedDownloadUrlAsync(new PresignedUrlRequest
{
    Path      = StoragePath.From("invoices", "invoice-001.pdf"),
    ExpiresIn = TimeSpan.FromMinutes(30)
});

// Upload URL — client uploads directly to Azure
var upload = await storage.GetPresignedUploadUrlAsync(new PresignedUrlRequest
{
    Path      = StoragePath.From("uploads", "video.mp4"),
    ExpiresIn = TimeSpan.FromMinutes(15)
});
```

### Resumable upload

```csharp
var session = await resumable.StartUploadAsync(new ResumableUploadRequest
{
    FileName    = "backup.zip",
    ContentType = "application/zip",
    TotalSize   = totalBytes
});

// Upload chunks sequentially or with retry
foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
{
    await resumable.UploadChunkAsync(new ResumableChunkRequest
    {
        SessionId  = session.SessionId,
        ChunkIndex = index,
        Data       = chunk
    });
}

await resumable.CompleteUploadAsync(session.SessionId);
```

---

## Features

| Feature | Supported |
|---|---|
| Upload / Download / Delete / List | Yes |
| Exists check | Yes |
| Copy / Move | Yes |
| Presigned upload URL (SAS) | Yes |
| Presigned download URL (SAS) | Yes |
| Resumable chunked uploads | Yes |
| BucketOverride per request | Yes |
| Automatic container creation | Yes (opt-in) |
| Managed Identity | Yes |
| Custom metadata | Yes |
| Polly retry resilience | Yes |

---

## Options reference

| Property | Default | Description |
|---|---|---|
| `Container` | — | Blob container name (required) |
| `ConnectionString` | — | Full Azure Storage connection string |
| `AccountName` | — | Storage account name (used with AccountKey or Managed Identity) |
| `AccountKey` | — | Storage account key |
| `CreateContainerIfNotExists` | `false` | Auto-create the container on startup |
| `SasTokenExpiryMinutes` | `60` | Default expiry for generated SAS tokens |

---

## Documentation

- [Azure Blob Storage provider docs](https://vali-blob-docs.netlify.app/docs/providers/azure)
- [Presigned URLs (SAS)](https://vali-blob-docs.netlify.app/docs/providers/azure#presigned-urls)
- [Resumable uploads](https://vali-blob-docs.netlify.app/docs/resumable-uploads)
- [Full documentation](https://vali-blob-docs.netlify.app)

---

## Links

- [GitHub Repository](https://github.com/UBF21/Vali-Blob)
- [NuGet Package](https://www.nuget.org/packages/ValiBlob.Azure)

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
