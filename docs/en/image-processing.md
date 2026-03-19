# Image Processing

`ValiBlob.ImageSharp` adds an image processing middleware to the upload pipeline. It can resize images, convert them to a different format (JPEG, PNG, WebP), and automatically generate thumbnails — all in a single upload operation.

---

## Installation

```bash
dotnet add package ValiBlob.ImageSharp
```

---

## Registration

```csharp
using ValiBlob.ImageSharp.DependencyInjection;

builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(p => p
        .WithImageProcessing(o =>
        {
            o.MaxWidth = 1920;
            o.MaxHeight = 1080;
            o.JpegQuality = 85;
        })
    );
```

---

## `ImageProcessingOptions`

| Property | Type | Default | Description |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Master on/off switch |
| `MaxWidth` | `int?` | `null` | Maximum output width in pixels. `null` = no limit |
| `MaxHeight` | `int?` | `null` | Maximum output height in pixels. `null` = no limit |
| `JpegQuality` | `int` | `85` | JPEG encoding quality (1–100) |
| `OutputFormat` | `ImageOutputFormat?` | `null` | Convert to `Jpeg`, `Png`, or `Webp`. `null` = keep original format |
| `ProcessableContentTypes` | `HashSet<string>` | See below | MIME types that trigger processing |
| `Thumbnail` | `ThumbnailOptions?` | `null` | Thumbnail generation settings. `null` = disabled |

Default processable content types: `image/jpeg`, `image/png`, `image/gif`, `image/bmp`, `image/webp`, `image/tiff`.

### `ThumbnailOptions`

| Property | Type | Default | Description |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Whether to generate a thumbnail |
| `Width` | `int` | `200` | Thumbnail width in pixels |
| `Height` | `int` | `200` | Thumbnail height in pixels |
| `Suffix` | `string` | `"_thumb"` | Suffix added to the filename. `photo.jpg` → `photo_thumb.jpg` |

### `ImageOutputFormat`

| Value | Output MIME type |
|---|---|
| `Jpeg` | `image/jpeg` |
| `Png` | `image/png` |
| `Webp` | `image/webp` |

---

## Examples

### Resize only — keep original format

```csharp
.WithImageProcessing(o =>
{
    o.MaxWidth = 2048;
    o.MaxHeight = 2048;
    // OutputFormat is null → format unchanged
})
```

Resizing uses `ResizeMode.Max`: the image is scaled proportionally to fit within the box. An image that already fits within the bounds is not upscaled.

### Convert all uploads to WebP

```csharp
.WithImageProcessing(o =>
{
    o.MaxWidth = 1920;
    o.MaxHeight = 1080;
    o.OutputFormat = ImageOutputFormat.Webp;
})
```

The `ContentType` on the request is updated to `image/webp` after conversion so the provider stores the correct MIME type.

### Generate thumbnails

```csharp
.WithImageProcessing(o =>
{
    o.MaxWidth = 1280;
    o.JpegQuality = 80;
    o.Thumbnail = new ThumbnailOptions
    {
        Enabled = true,
        Width = 300,
        Height = 300,
        Suffix = "_thumb"
    };
})
```

When thumbnail generation is enabled, the middleware:

1. Processes and uploads the main image through the pipeline as usual.
2. After the main upload completes, generates a JPEG thumbnail at the specified dimensions.
3. Uploads the thumbnail to the same storage provider at a derived path: `{dir}/{name}{suffix}.jpg`.

For example, uploading `products/chair.png` also creates `products/chair_thumb.jpg`.

Thumbnail upload failures are **non-fatal** — an error during thumbnail generation is silently ignored so the main upload is not affected.

### Restrict processing to specific formats

```csharp
.WithImageProcessing(o =>
{
    o.MaxWidth = 800;
    o.ProcessableContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png"
        // GIF, BMP, TIFF, WebP will pass through unmodified
    };
})
```

Files whose `ContentType` is not in `ProcessableContentTypes` are passed through the middleware without modification.

---

## Pipeline position

Place `WithImageProcessing` after `ContentTypeDetectionMiddleware` (so the MIME type is accurate) and before any validation that checks image dimensions or size:

```csharp
.WithPipeline(p => p
    .WithContentTypeDetection(o => o.OverrideExisting = true)
    .WithImageProcessing(o =>
    {
        o.MaxWidth = 1920;
        o.OutputFormat = ImageOutputFormat.Webp;
    })
    .UseValidation(v =>
    {
        v.AllowedContentTypes = new[] { "image/webp" }; // validate the output type
    })
)
```

---

## Performance note

Images are loaded and processed entirely in memory using the [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) library. For large images or high-concurrency scenarios, this can have a significant memory footprint. Consider:

- Setting reasonable `MaxWidth` / `MaxHeight` limits to cap output size.
- Offloading image processing to a dedicated background job for very large files.
- Monitoring heap pressure under load and adjusting the application memory limits accordingly.
