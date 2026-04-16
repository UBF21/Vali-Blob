# Vali-Blob.Supabase

[![NuGet](https://img.shields.io/nuget/v/ValiBlob.Supabase.svg)](https://www.nuget.org/packages/ValiBlob.Supabase)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](https://github.com/UBF21/Vali-Blob/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6%20%7C%207%20%7C%208%20%7C%209-purple)](https://www.nuget.org/packages/ValiBlob.Supabase)

Supabase Storage provider for **Vali-Blob** — the unified cloud storage abstraction library for .NET.

Implements `IStorageProvider` over the Supabase Storage REST API with signed URL generation, resumable uploads via the **TUS protocol**, public and private bucket support, and seamless DI registration.

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
dotnet add package ValiBlob.Supabase
```

---

## Configuration

```json
{
  "ValiBlob": {
    "DefaultProvider": "Supabase"
  },
  "ValiBlob:Supabase": {
    "Url":    "https://your-project-ref.supabase.co",
    "ApiKey": "",
    "Bucket": "my-bucket"
  }
}
```

> **Security:** Never commit `ApiKey` to source control. Use environment variables or a secrets manager.
> Use the `service_role` key for server-side operations with full access. For user-facing operations, use a scoped anon key with Row Level Security (RLS) policies configured in Supabase.

---

## Registration

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Supabase.Extensions;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "Supabase")
    .UseSupabase();
```

### With pipeline

```csharp
builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "Supabase")
    .UseSupabase()
    .WithPipeline(p => p
        .UseValidation(v =>
        {
            v.AllowedExtensions = new[] { ".jpg", ".png", ".gif", ".webp" };
            v.MaxFileSizeBytes  = 10 * 1024 * 1024; // 10 MB
        })
    );
```

---

## Usage

### Upload

```csharp
public class AvatarService(IStorageProvider storage)
{
    public async Task<string> UploadAvatarAsync(Stream image, string userId, string extension)
    {
        var result = await storage.UploadAsync(new UploadRequest
        {
            Path        = StoragePath.From("avatars", $"{userId}{extension}"),
            Content     = image,
            ContentType = "image/jpeg"
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
    Path = StoragePath.From("avatars", "user-123.jpg")
});

if (result.IsSuccess)
    await result.Value!.CopyToAsync(Response.Body);
```

### Public bucket URL

For objects in a public bucket, the URL is returned directly in the upload result:

```csharp
var result = await storage.UploadAsync(request);
var publicUrl = result.Value!.Url;
// → "https://your-project.supabase.co/storage/v1/object/public/avatars/user-123.jpg"
```

### Signed URL (private bucket)

```csharp
var url = await storage.GetPresignedDownloadUrlAsync(new PresignedUrlRequest
{
    Path      = StoragePath.From("private-docs", "contract.pdf"),
    ExpiresIn = TimeSpan.FromHours(1)
});

return Redirect(url.Value!);
```

### Resumable upload (TUS protocol)

Supabase Storage implements the [TUS resumable upload protocol](https://tus.io). Use this for large files — the upload can be paused and resumed after network interruptions:

```csharp
public class UploadService(IResumableUploadProvider resumable)
{
    public async Task UploadVideoAsync(Stream content, string fileName, long totalSize)
    {
        // Start session — returns a TUS upload URL
        var session = await resumable.StartUploadAsync(new ResumableUploadRequest
        {
            FileName    = fileName,
            ContentType = "video/mp4",
            TotalSize   = totalSize
        });

        // Upload in chunks (e.g. 6 MB each — Supabase default chunk size)
        var buffer = new byte[6 * 1024 * 1024];
        int chunkIndex = 0, read;

        while ((read = await content.ReadAsync(buffer)) > 0)
        {
            await resumable.UploadChunkAsync(new ResumableChunkRequest
            {
                SessionId  = session.SessionId,
                ChunkIndex = chunkIndex++,
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
| Public bucket URLs | Yes |
| Signed download URL | Yes |
| Resumable uploads (TUS protocol) | Yes |
| BucketOverride per request | Yes |
| RLS / scoped API key support | Yes |
| Polly retry resilience | Yes |

---

## Options reference

| Property | Default | Description |
|---|---|---|
| `Url` | — | Supabase project URL (required) |
| `ApiKey` | — | Supabase API key (`service_role` or `anon`) |
| `Bucket` | — | Default storage bucket name |
| `SignedUrlExpirySeconds` | `3600` | Default signed URL expiry in seconds |

---

## Documentation

- [Supabase Storage provider docs](https://vali-blob-docs.netlify.app/docs/providers/supabase)
- [Resumable uploads (TUS)](https://vali-blob-docs.netlify.app/docs/resumable-uploads)
- [Signed URLs](https://vali-blob-docs.netlify.app/docs/providers/supabase#presigned-urls)
- [Full documentation](https://vali-blob-docs.netlify.app)

---

## Links

- [GitHub Repository](https://github.com/UBF21/Vali-Blob)
- [NuGet Package](https://www.nuget.org/packages/ValiBlob.Supabase)

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
