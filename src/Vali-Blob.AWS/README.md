# Vali-Blob.AWS

[![NuGet](https://img.shields.io/nuget/v/ValiBlob.AWS.svg)](https://www.nuget.org/packages/ValiBlob.AWS)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](https://github.com/UBF21/Vali-Blob/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6%20%7C%207%20%7C%208%20%7C%209-purple)](https://www.nuget.org/packages/ValiBlob.AWS)

Amazon S3 and MinIO provider for **Vali-Blob** — the unified cloud storage abstraction library for .NET.

Implements `IStorageProvider` over AWS S3 with automatic multipart upload for large files, presigned URL generation, resumable chunked uploads, and full MinIO compatibility for self-hosted deployments.

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
dotnet add package ValiBlob.AWS
```

---

## Configuration

### `appsettings.json` — AWS S3

```json
{
  "ValiBlob": {
    "DefaultProvider": "AWS"
  },
  "ValiBlob:AWS": {
    "Bucket":          "my-app-files",
    "Region":          "us-east-1",
    "AccessKeyId":     "",
    "SecretAccessKey": ""
  }
}
```

> **Security:** Never commit `AccessKeyId` or `SecretAccessKey` to source control. Use environment variables, AWS Secrets Manager, or instance profiles. On EC2 / ECS / Lambda, leave both fields empty — the SDK picks up the instance role automatically.

### `appsettings.json` — MinIO

```json
{
  "ValiBlob": {
    "DefaultProvider": "AWS"
  },
  "ValiBlob:AWS": {
    "Endpoint":        "http://localhost:9000",
    "Bucket":          "my-bucket",
    "AccessKeyId":     "minioadmin",
    "SecretAccessKey": "minioadmin",
    "ForcePathStyle":  true
  }
}
```

---

## Registration

### AWS S3

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.AWS.Extensions;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseAWS();
```

### MinIO (self-hosted)

```csharp
builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseMinIO(opts =>
    {
        opts.Endpoint        = "http://localhost:9000";
        opts.Bucket          = "my-bucket";
        opts.AccessKeyId     = "minioadmin";
        opts.SecretAccessKey = "minioadmin";
    });
```

### With pipeline

```csharp
builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseAWS()
    .WithPipeline(p => p
        .UseValidation(v =>
        {
            v.AllowedExtensions = new[] { ".jpg", ".png", ".pdf" };
            v.MaxFileSizeBytes  = 50 * 1024 * 1024; // 50 MB
        })
        .UseCompression()
    );
```

---

## Usage

### Upload

```csharp
public class DocumentService(IStorageProvider storage)
{
    public async Task<string> UploadAsync(Stream content, string fileName)
    {
        var result = await storage.UploadAsync(new UploadRequest
        {
            Path        = StoragePath.From("documents", fileName),
            Content     = content,
            ContentType = "application/pdf"
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
    Path = StoragePath.From("documents", "report.pdf")
});

if (result.IsSuccess)
{
    using var stream = result.Value!;
    await stream.CopyToAsync(Response.Body);
}
```

### Presigned URL

```csharp
var url = await storage.GetPresignedDownloadUrlAsync(new PresignedUrlRequest
{
    Path      = StoragePath.From("documents", "report.pdf"),
    ExpiresIn = TimeSpan.FromHours(1)
});

Console.WriteLine($"Temporary link: {url.Value}");
```

### Resumable upload

```csharp
public class ResumableService(IResumableUploadProvider resumable)
{
    public async Task UploadLargeFileAsync(Stream content, string fileName, long totalSize)
    {
        var session = await resumable.StartUploadAsync(new ResumableUploadRequest
        {
            FileName    = fileName,
            ContentType = "video/mp4",
            TotalSize   = totalSize
        });

        // Upload in 5 MB chunks
        var buffer = new byte[5 * 1024 * 1024];
        int index = 0, read;

        while ((read = await content.ReadAsync(buffer)) > 0)
        {
            await resumable.UploadChunkAsync(new ResumableChunkRequest
            {
                SessionId  = session.SessionId,
                ChunkIndex = index++,
                Data       = new MemoryStream(buffer, 0, read)
            });
        }

        await resumable.CompleteUploadAsync(session.SessionId);
    }
}
```

---

## Features

| Feature | Supported |
|---|---|
| Upload / Download / Delete / List | Yes |
| Exists check | Yes |
| Copy / Move | Yes |
| Automatic multipart (files > threshold) | Yes |
| Presigned upload URL | Yes |
| Presigned download URL | Yes |
| Resumable chunked uploads | Yes |
| BucketOverride per request | Yes |
| MinIO compatibility | Yes |
| Custom metadata | Yes |
| Polly retry resilience | Yes |

---

## Options reference

| Property | Default | Description |
|---|---|---|
| `Bucket` | — | S3 bucket name (required) |
| `Region` | `us-east-1` | AWS region |
| `AccessKeyId` | — | AWS access key (leave empty for instance profile) |
| `SecretAccessKey` | — | AWS secret key |
| `Endpoint` | — | Custom endpoint URL (MinIO, LocalStack, etc.) |
| `ForcePathStyle` | `false` | Use path-style URLs (required for MinIO) |
| `MultipartThresholdBytes` | `100 MB` | Files above this size use multipart upload |
| `MultipartPartSizeBytes` | `10 MB` | Size of each multipart part |

---

## Documentation

- [AWS S3 / MinIO provider docs](https://vali-blob-docs.netlify.app/docs/providers/aws)
- [Presigned URLs](https://vali-blob-docs.netlify.app/docs/providers/aws#presigned-urls)
- [Resumable uploads](https://vali-blob-docs.netlify.app/docs/resumable-uploads)
- [Full documentation](https://vali-blob-docs.netlify.app)

---

## Links

- [GitHub Repository](https://github.com/UBF21/Vali-Blob)
- [NuGet Package](https://www.nuget.org/packages/ValiBlob.AWS)

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
