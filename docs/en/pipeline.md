# Pipeline and Middleware

ValiBlob's upload pipeline is a composable middleware chain that processes every `UploadRequest` before it reaches the underlying cloud provider. It follows the same design pattern as ASP.NET Core's request pipeline: each middleware can inspect, transform, or short-circuit the request, and then optionally call `next` to pass control to the following middleware.

---

## How the pipeline works

```
UploadAsync(request)
        │
        ▼
┌───────────────────┐
│ ValidationMiddleware │  ← Rejects invalid files early
└─────────┬─────────┘
          │ next()
          ▼
┌───────────────────────┐
│ CompressionMiddleware  │  ← Compresses text/JSON/XML content
└─────────┬─────────────┘
          │ next()
          ▼
┌───────────────────────┐
│ EncryptionMiddleware   │  ← AES-256-CBC client-side encryption
└─────────┬─────────────┘
          │ next()
          ▼
┌─────────────────────────────┐
│ Cloud Provider (S3/Azure/…)  │
└─────────────────────────────┘
```

The pipeline is built once per application lifetime from the registered `IStorageMiddleware` services and stored in `StoragePipelineBuilder`.

---

## Built-in middlewares

| Middleware | Class | Method to register |
|---|---|---|
| File validation | `ValidationMiddleware` | `.UseValidation()` |
| GZip compression | `CompressionMiddleware` | `.UseCompression()` |
| AES-256 encryption | `EncryptionMiddleware` | `.UseEncryption()` |

---

## `ValidationMiddleware`

Validates every `UploadRequest` against the configured `ValidationOptions`. If validation fails, the upload is rejected immediately with `StorageErrorCode.ValidationFailed` — the provider is never called.

### `ValidationOptions` reference

| Property | Type | Default | Description |
|---|---|---|---|
| `MaxFileSizeBytes` | `long` | `524288000` (500 MB) | Maximum allowed file size in bytes |
| `AllowedExtensions` | `IList<string>` | `[]` (allow all) | When non-empty, only these extensions are accepted |
| `BlockedExtensions` | `IList<string>` | `[".exe", ".bat", ".cmd", ".sh"]` | Extensions that are always rejected |
| `AllowedContentTypes` | `IList<string>` | `[]` (allow all) | When non-empty, only these MIME types are accepted |

When `AllowedExtensions` is empty, all extensions are permitted except those in `BlockedExtensions`.

### Configuration examples

```csharp
// Via code
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(p =>
        p.UseValidation(v =>
        {
            v.MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
            v.AllowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            v.AllowedContentTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        })
    );
```

```json
// Via appsettings.json
{
  "ValiBlob": {
    "Validation": {
      "MaxFileSizeBytes": 10485760,
      "AllowedExtensions": [ ".jpg", ".jpeg", ".png", ".gif", ".webp" ],
      "AllowedContentTypes": [ "image/jpeg", "image/png", "image/gif", "image/webp" ],
      "BlockedExtensions": [ ".exe", ".bat", ".cmd", ".sh", ".ps1" ]
    }
  }
}
```

### Handling validation failures

```csharp
var result = await _storage.UploadAsync(request);

if (!result.IsSuccess && result.ErrorCode == StorageErrorCode.ValidationFailed)
{
    // result.ErrorMessage contains a human-readable description
    return BadRequest(new { error = result.ErrorMessage });
}
```

---

## `CompressionMiddleware`

Transparently compresses the content stream using GZip before upload. The original `ContentType` and `ContentLength` are preserved in the request for metadata purposes.

### When compression activates

Compression is applied only when **all** of the following conditions are met:

1. `CompressionOptions.Enabled` is `true`
2. The file size exceeds `MinSizeBytes` (default: 1024 bytes)
3. The `ContentType` is in the `CompressibleContentTypes` list

### `CompressionOptions` reference

| Property | Type | Default | Description |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Master on/off switch |
| `MinSizeBytes` | `int` | `1024` | Only compress files larger than this |
| `CompressibleContentTypes` | `IList<string>` | See below | MIME types eligible for compression |

Default compressible content types:
- `text/plain`
- `text/html`
- `text/css`
- `text/xml`
- `application/json`
- `application/xml`
- `application/javascript`

### Configuration example

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(p =>
        p.UseCompression(c =>
        {
            c.Enabled = true;
            c.MinSizeBytes = 4096; // Only compress files > 4 KB
            c.CompressibleContentTypes = new[]
            {
                "text/plain", "application/json", "text/csv", "application/xml"
            };
        })
    );
```

> **💡 Tip:** Do not compress content types that are already compressed (e.g., `image/jpeg`, `image/png`, `application/zip`, `video/mp4`). Attempting to GZip already-compressed data can make it slightly larger.

---

## `EncryptionMiddleware`

Encrypts the content stream using **AES-256-CBC** before upload. The encryption is transparent — the caller uploads and downloads the same logical bytes; ValiBlob handles encryption on upload and decryption on download.

### `EncryptionOptions` reference

| Property | Type | Default | Description |
|---|---|---|---|
| `Enabled` | `bool` | `false` | Must be explicitly set to `true` |
| `Key` | `byte[]?` | `null` | 32-byte (256-bit) AES key |
| `IV` | `byte[]?` | `null` | 16-byte initialization vector. If `null`, a random IV is generated per upload |

### Key management

The encryption key must be exactly **32 bytes** (256 bits). Store it in a secrets manager, not in `appsettings.json`.

```csharp
// Generate a new key (do this once, then store securely)
using var aes = System.Security.Cryptography.Aes.Create();
aes.KeySize = 256;
aes.GenerateKey();
var base64Key = Convert.ToBase64String(aes.Key);
Console.WriteLine(base64Key); // Store this securely!
```

Load the key at runtime from a secret store:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(p =>
        p.UseEncryption(e =>
        {
            e.Enabled = true;
            e.Key = Convert.FromBase64String(
                builder.Configuration["ValiBlob:EncryptionKey"]!);
            // IV is null → random IV generated per upload (recommended)
        })
    );
```

### Fixed vs random IV

- **Random IV (default, `IV = null`)**: A new random 16-byte IV is generated for every upload. The IV is prepended to the encrypted content so it can be recovered during download. This provides the best security — identical files produce different ciphertexts.
- **Fixed IV**: Provide a 16-byte array. Identical files always produce identical ciphertexts. Use only when deterministic deduplication is required and you understand the security trade-off.

> **⚠️ Warning:** Using a fixed IV significantly weakens encryption when the same key is reused across many files. Prefer random IV for all production scenarios.

---

## Writing a custom middleware

Implement `IStorageMiddleware` and register it with `.Use<T>()`.

```csharp
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Pipeline;

public sealed class WatermarkMiddleware : IStorageMiddleware
{
    private readonly ILogger<WatermarkMiddleware> _logger;

    public WatermarkMiddleware(ILogger<WatermarkMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next)
    {
        // Inspect the request
        _logger.LogDebug("Processing upload for path: {Path}", context.Request.Path);

        // You can add custom data to the context for downstream middleware
        context.Items["processing-start"] = DateTimeOffset.UtcNow;

        // Optionally short-circuit (reject the upload without calling next)
        if (context.Request.Path.FileName.StartsWith("_private_"))
        {
            context.IsCancelled = true;
            context.CancellationReason = "Files beginning with '_private_' are not allowed.";
            return; // do not call next
        }

        // Transform the request before upload
        // (e.g., replace the content stream with a modified version)
        if (context.Request.ContentType == "image/png")
        {
            var watermarked = await AddWatermarkAsync(context.Request.Content);
            context.Request = context.Request.WithContent(watermarked);
        }

        // Call next middleware (or the provider if this is last)
        await next(context);

        // Post-processing after upload completes
        var startTime = (DateTimeOffset)context.Items["processing-start"];
        _logger.LogDebug("Upload processed in {Elapsed}ms", (DateTimeOffset.UtcNow - startTime).TotalMilliseconds);
    }

    private Task<Stream> AddWatermarkAsync(Stream original)
    {
        // Your watermarking logic here
        return Task.FromResult(original);
    }
}
```

### Registering a custom middleware

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(p => p
        .UseValidation()
        .UseCompression()
        .Use<WatermarkMiddleware>()   // custom middleware last
    );
```

Custom middleware classes are resolved from the DI container, so they can receive any registered service via constructor injection.

---

## Pipeline order matters

The order in which middlewares are registered determines the order in which they execute. The general recommended order is:

1. **Validation** — reject invalid files before spending resources on them
2. **Compression** — compress content before encrypting (compression is far less effective after encryption)
3. **Encryption** — encrypt the final compressed bytes
4. **Custom middlewares** — e.g., watermarking, audit logging, content scanning

```csharp
.WithPipeline(p => p
    .UseValidation()     // 1. Reject bad files immediately
    .UseCompression()    // 2. Compress valid content
    .UseEncryption()     // 3. Encrypt compressed content
    .Use<AuditMiddleware>() // 4. Log the completed transformation
)
```

> **⚠️ Warning:** Registering encryption before compression means you are compressing already-encrypted data, which is essentially random bytes and will not compress at all (or may slightly increase in size). Always compress before encrypting.

---

## `StoragePipelineContext` reference

| Property | Type | Description |
|---|---|---|
| `Request` | `UploadRequest` | The current upload request — can be replaced by middleware |
| `Items` | `IDictionary<string, object>` | Arbitrary key-value store shared across middleware in the pipeline |
| `IsCancelled` | `bool` | Set to `true` to short-circuit the pipeline |
| `CancellationReason` | `string?` | Human-readable reason for cancellation, surfaced in the result |
