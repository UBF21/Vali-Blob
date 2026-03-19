# Content-Type Detection

The `ContentTypeDetectionMiddleware` inspects the leading bytes (magic bytes) of every uploaded file and sets — or overrides — the `ContentType` field on the request. This prevents callers from disguising dangerous files by lying about their extension or MIME type.

---

## Why use it

File extensions and caller-supplied `Content-Type` headers are trivially spoofed. An attacker can rename `malware.exe` to `invoice.jpg` and upload it with `Content-Type: image/jpeg`. Without magic-byte inspection, that file passes any extension or MIME-type allowlist.

`ContentTypeDetectionMiddleware` reads the first 16 bytes of the stream to detect the actual format and overwrites (or supplies) the `ContentType` before any downstream middleware — including `ValidationMiddleware` — sees it. This means your allowlist enforces real file formats, not just filenames.

---

## Supported formats

| Format | MIME type | Magic bytes |
|---|---|---|
| JPEG | `image/jpeg` | `FF D8 FF` |
| PNG | `image/png` | `89 50 4E 47` |
| GIF | `image/gif` | `47 49 46` |
| BMP | `image/bmp` | `42 4D` |
| TIFF (LE) | `image/tiff` | `49 49 2A 00` |
| TIFF (BE) | `image/tiff` | `4D 4D 00 2A` |
| PDF | `application/pdf` | `25 50 44 46` |
| ZIP / DOCX / XLSX | `application/zip` | `50 4B 03 04` |
| GZip | `application/gzip` | `1F 8B` |
| RAR | `application/x-rar` | `52 61 72 21` |
| MP4 | `video/mp4` | `ftyp` at offset 4 |
| MP3 (ID3) | `audio/mpeg` | `49 44 33` |
| MP3 (sync) | `audio/mpeg` | `FF FB` or `FF F3` |

If the magic bytes do not match any known format, `ContentType` is left unchanged.

---

## Configuration

| Property | Type | Default | Description |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Master on/off switch |
| `OverrideExisting` | `bool` | `false` | When `false`, detection only runs if `ContentType` is `null`. When `true`, detection always runs and overwrites any caller-supplied value. |

---

## Registration

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(p => p
        .UseValidation(v =>
        {
            v.AllowedContentTypes = new[] { "image/jpeg", "image/png", "application/pdf" };
        })
        .WithContentTypeDetection(o =>
        {
            o.Enabled = true;
            o.OverrideExisting = true; // always trust magic bytes, not caller claims
        })
    );
```

> When `OverrideExisting` is `false` (the default), the middleware acts as a fallback: it sets `ContentType` only when the caller did not supply one. This is safe for trusted internal callers. Set it to `true` when uploads originate from untrusted clients.

---

## Non-seekable streams

When the input stream does not support `Seek` (e.g., a network socket or a compressed stream passed through `CompressionMiddleware`), the middleware reads the first 16 bytes into a buffer and wraps the original stream in a `LeadingBytesStream`. This re-plays those leading bytes to downstream middleware and the provider so no data is lost. No special handling is required from your code.

---

## Combining with ValidationMiddleware

Register `ContentTypeDetectionMiddleware` **before** `ValidationMiddleware` in the pipeline so that validation sees the corrected MIME type:

```csharp
.WithPipeline(p => p
    .WithContentTypeDetection(o => o.OverrideExisting = true)  // 1. detect real type
    .UseValidation(v =>
    {
        v.AllowedContentTypes = new[] { "image/jpeg", "image/png" }; // 2. enforce it
    })
)
```

Reversing this order means `ValidationMiddleware` checks the caller-supplied (potentially spoofed) `ContentType` before it has been corrected.
