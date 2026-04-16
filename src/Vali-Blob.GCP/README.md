# Vali-Blob.GCP

[![NuGet](https://img.shields.io/nuget/v/ValiBlob.GCP.svg)](https://www.nuget.org/packages/ValiBlob.GCP)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](https://github.com/UBF21/Vali-Blob/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6%20%7C%207%20%7C%208%20%7C%209-purple)](https://www.nuget.org/packages/ValiBlob.GCP)

Google Cloud Storage provider for **Vali-Blob** — the unified cloud storage abstraction library for .NET.

Implements `IStorageProvider` over GCS with signed URL generation, resumable uploads, Application Default Credentials support, and seamless DI registration.

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
dotnet add package ValiBlob.GCP
```

---

## Configuration

### With a service account credentials file (recommended)

```json
{
  "ValiBlob": {
    "DefaultProvider": "GCP"
  },
  "ValiBlob:GCP": {
    "Bucket":          "my-app-bucket",
    "CredentialsPath": "/run/secrets/gcp-service-account.json"
  }
}
```

### With credentials JSON inline

```json
{
  "ValiBlob:GCP": {
    "Bucket":          "my-app-bucket",
    "CredentialsJson": "{ \"type\": \"service_account\", \"project_id\": \"...\", ... }"
  }
}
```

### With Application Default Credentials (ADC)

```json
{
  "ValiBlob:GCP": {
    "Bucket": "my-app-bucket"
  }
}
```

> **Note:** When using ADC (Compute Engine, Cloud Run, GKE Workload Identity), most operations work out of the box. However, **presigned (signed) URLs require a service account key** — ADC does not carry the private key needed for V4 signing. Provide `CredentialsPath` or `CredentialsJson` to enable presigned URLs.

---

## Registration

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.GCP.Extensions;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "GCP")
    .UseGCP();
```

### With pipeline

```csharp
builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "GCP")
    .UseGCP()
    .WithPipeline(p => p
        .UseValidation(v =>
        {
            v.AllowedExtensions = new[] { ".jpg", ".png", ".mp4", ".pdf" };
            v.MaxFileSizeBytes  = 500 * 1024 * 1024; // 500 MB
        })
    );
```

---

## Usage

### Upload

```csharp
public class MediaService(IStorageProvider storage)
{
    public async Task<string> UploadAsync(Stream content, string fileName, string contentType)
    {
        var result = await storage.UploadAsync(new UploadRequest
        {
            Path        = StoragePath.From("media", fileName),
            Content     = content,
            ContentType = contentType
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
    Path = StoragePath.From("media", "photo.jpg")
});

if (result.IsSuccess)
    await result.Value!.CopyToAsync(outputStream);
```

### Signed URL (requires service account)

```csharp
// Presigned download URL — valid for 2 hours
var url = await storage.GetPresignedDownloadUrlAsync(new PresignedUrlRequest
{
    Path      = StoragePath.From("reports", "annual-2024.pdf"),
    ExpiresIn = TimeSpan.FromHours(2)
});

if (url.IsSuccess)
    return Redirect(url.Value!);
else
    return BadRequest(url.ErrorMessage); // "NotSupported" if ADC is used
```

### Resumable upload

```csharp
var session = await resumable.StartUploadAsync(new ResumableUploadRequest
{
    FileName    = "dataset.zip",
    ContentType = "application/zip",
    TotalSize   = totalBytes
});

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
| Signed upload URL | Yes (service account required) |
| Signed download URL | Yes (service account required) |
| Resumable chunked uploads | Yes |
| BucketOverride per request | Yes |
| Application Default Credentials | Yes |
| Custom metadata | Yes |
| Polly retry resilience | Yes |

---

## Options reference

| Property | Default | Description |
|---|---|---|
| `Bucket` | — | GCS bucket name (required) |
| `CredentialsPath` | — | Path to service account JSON key file |
| `CredentialsJson` | — | Service account JSON content as a string |
| `SignedUrlExpiryMinutes` | `60` | Default expiry for generated signed URLs |

---

## Documentation

- [GCP Cloud Storage provider docs](https://vali-blob-docs.netlify.app/docs/providers/gcp)
- [Presigned (signed) URLs](https://vali-blob-docs.netlify.app/docs/providers/gcp#presigned-urls)
- [Resumable uploads](https://vali-blob-docs.netlify.app/docs/resumable-uploads)
- [Full documentation](https://vali-blob-docs.netlify.app)

---

## Links

- [GitHub Repository](https://github.com/UBF21/Vali-Blob)
- [NuGet Package](https://www.nuget.org/packages/ValiBlob.GCP)

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
