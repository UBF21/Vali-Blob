# Vali-Blob.ImageSharp

[![NuGet](https://img.shields.io/nuget/v/ValiBlob.ImageSharp.svg)](https://www.nuget.org/packages/ValiBlob.ImageSharp)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](https://github.com/UBF21/Vali-Blob/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6%20%7C%207%20%7C%208%20%7C%209-purple)](https://www.nuget.org/packages/ValiBlob.ImageSharp)

Image processing middleware for **Vali-Blob** using [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp).

Plugs into the Vali-Blob middleware pipeline and automatically resizes, converts, and optimizes images before they are stored via any Vali-Blob provider (AWS S3, Azure Blob, GCP, OCI, Supabase, Local). Optionally generates a thumbnail alongside the main upload — all in a single pipeline step.

---

## Compatibility

| Target Framework | Supported |
|---|---|
| `net6.0` | Yes |
| `net7.0` | Yes |
| `net8.0` | Yes |
| `net9.0` | Yes |

---

## Installation

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.ImageSharp
```

---

## Registration

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.AWS.Extensions;
using ValiBlob.ImageSharp.Extensions;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseAWS()
    .WithImageProcessing(opts =>
    {
        opts.MaxWidth     = 1920;
        opts.MaxHeight    = 1080;
        opts.JpegQuality  = 85;
        opts.OutputFormat = ImageOutputFormat.Jpeg;
    });
```

### With thumbnail generation

```csharp
builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseAWS()
    .WithImageProcessing(opts =>
    {
        opts.MaxWidth     = 2048;
        opts.MaxHeight    = 2048;
        opts.JpegQuality  = 80;
        opts.OutputFormat = ImageOutputFormat.WebP;

        opts.Thumbnail = new ThumbnailOptions
        {
            Width  = 300,
            Height = 300,
            Suffix = "_thumb"  // stored as "uploads/photo_thumb.webp"
        };
    });
```

### Combined with validation

```csharp
builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "Azure")
    .UseAzure()
    .WithPipeline(p => p
        .UseValidation(v =>
        {
            v.AllowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
            v.MaxFileSizeBytes  = 20 * 1024 * 1024; // 20 MB before processing
        })
    )
    .WithImageProcessing(opts =>
    {
        opts.MaxWidth     = 1920;
        opts.MaxHeight    = 1080;
        opts.JpegQuality  = 82;
        opts.OutputFormat = ImageOutputFormat.Jpeg;

        opts.Thumbnail = new ThumbnailOptions
        {
            Width  = 200,
            Height = 200,
            Suffix = "_sm"
        };
    });
```

---

## Usage

Once registered, image processing is fully transparent — just upload normally:

```csharp
public class PhotoService(IStorageProvider storage)
{
    public async Task<PhotoResult> UploadPhotoAsync(IFormFile file, string userId)
    {
        await using var stream = file.OpenReadStream();

        var result = await storage.UploadAsync(new UploadRequest
        {
            Path        = StoragePath.From("photos", userId, file.FileName),
            Content     = stream,
            ContentType = file.ContentType
        });

        if (!result.IsSuccess)
            throw new Exception(result.ErrorMessage);

        // Image was automatically resized + converted + thumbnail generated
        return new PhotoResult
        {
            Url          = result.Value!.Url,
            ThumbnailUrl = result.Value!.ThumbnailUrl  // populated when opts.Thumbnail is set
        };
    }
}
```

---

## How the pipeline works

```
Client upload (original JPEG, 8 MB, 4000×3000)
        │
        ▼
┌───────────────────────┐
│  ValidationMiddleware  │  ← reject if wrong extension / too large
└───────────┬───────────┘
            │
            ▼
┌───────────────────────┐
│  ImageSharpMiddleware  │  ← resize → 1920×1080, convert → WebP, quality 80
└───────────┬───────────┘        also generates 300×300 thumbnail
            │
            ▼
┌───────────────────────┐
│     Cloud Provider     │  ← store processed image + thumbnail
└───────────────────────┘
```

Non-image content types (e.g. PDF, ZIP) pass through unchanged.

---

## Supported input formats

| Format | Processed |
|---|---|
| JPEG / JPG | Yes |
| PNG | Yes |
| GIF | Yes |
| BMP | Yes |
| WebP | Yes |
| TIFF | Yes |
| Other (PDF, ZIP, etc.) | No — passed through unchanged |

---

## Output formats

| `ImageOutputFormat` | Description |
|---|---|
| `Jpeg` | JPEG with configurable quality |
| `Png` | PNG (lossless) |
| `WebP` | WebP — best compression for web |
| `Original` | Keep the original format (only resize/crop) |

---

## Options reference

| Property | Default | Description |
|---|---|---|
| `MaxWidth` | `0` (no limit) | Maximum output width in pixels |
| `MaxHeight` | `0` (no limit) | Maximum output height in pixels |
| `JpegQuality` | `80` | JPEG quality 1–100 (higher = better quality, larger file) |
| `OutputFormat` | `Original` | Target output format |
| `ProcessableContentTypes` | `image/jpeg`, `image/png`, `image/gif`, `image/bmp`, `image/webp`, `image/tiff` | Content types that trigger processing |
| `Thumbnail.Width` | — | Thumbnail width in pixels |
| `Thumbnail.Height` | — | Thumbnail height in pixels |
| `Thumbnail.Suffix` | `"_thumb"` | Appended to the file name before the extension |

> **Aspect ratio:** Images are resized with aspect-ratio preservation (fit inside `MaxWidth` × `MaxHeight` without cropping). Thumbnails are cropped to fill the exact `Width` × `Height` specified.

---

## Features

| Feature | Supported |
|---|---|
| Resize with aspect-ratio preservation | Yes |
| Format conversion (JPEG, PNG, WebP) | Yes |
| JPEG quality control | Yes |
| Thumbnail generation (separate file) | Yes |
| Pass-through for non-image content | Yes |
| Configurable processable MIME types | Yes |
| Works with all Vali-Blob providers | Yes |

---

## Documentation

- [Image processing docs](https://vali-blob-docs.netlify.app/docs/image-processing)
- [Pipeline & Middleware](https://vali-blob-docs.netlify.app/docs/pipeline)
- [Full documentation](https://vali-blob-docs.netlify.app)

---

## Links

- [GitHub Repository](https://github.com/UBF21/Vali-Blob)
- [NuGet Package](https://www.nuget.org/packages/ValiBlob.ImageSharp)

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
