# Vali-Blob.OCI

[![NuGet](https://img.shields.io/nuget/v/ValiBlob.OCI.svg)](https://www.nuget.org/packages/ValiBlob.OCI)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](https://github.com/UBF21/Vali-Blob/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6%20%7C%207%20%7C%208%20%7C%209-purple)](https://www.nuget.org/packages/ValiBlob.OCI)

Oracle Cloud Infrastructure (OCI) Object Storage provider for **Vali-Blob** — the unified cloud storage abstraction library for .NET.

Implements `IStorageProvider` over OCI Object Storage with Pre-Authenticated Request (PAR) URL generation, resumable multipart uploads, and seamless DI registration.

> **OCI specifics:**
> - Presigned URLs use **Pre-Authenticated Requests (PARs)** — each URL requires an API call to OCI (no local signing like AWS/GCP).
> - Metadata can only be set **at upload time** — in-place metadata updates require re-uploading the object.

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
dotnet add package ValiBlob.OCI
```

---

## Configuration

```json
{
  "ValiBlob": {
    "DefaultProvider": "OCI"
  },
  "ValiBlob:OCI": {
    "Namespace":      "my-tenancy-namespace",
    "Bucket":         "my-bucket",
    "Region":         "us-ashburn-1",
    "TenancyId":      "ocid1.tenancy.oc1...",
    "UserId":         "ocid1.user.oc1...",
    "Fingerprint":    "xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx",
    "PrivateKeyPath": "/run/secrets/oci_api_key.pem"
  }
}
```

Alternatively, provide the private key content directly:

```json
{
  "ValiBlob:OCI": {
    "Namespace":          "my-tenancy-namespace",
    "Bucket":             "my-bucket",
    "Region":             "us-ashburn-1",
    "TenancyId":          "ocid1.tenancy.oc1...",
    "UserId":             "ocid1.user.oc1...",
    "Fingerprint":        "xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx",
    "PrivateKeyContent":  "-----BEGIN RSA PRIVATE KEY-----\n..."
  }
}
```

> **Security:** Store `PrivateKeyPath` or `PrivateKeyContent` via OCI Vault, environment variables, or a secrets manager. Never commit private keys to source control.

---

## Registration

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.OCI.Extensions;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "OCI")
    .UseOCI();
```

### With pipeline

```csharp
builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "OCI")
    .UseOCI()
    .WithPipeline(p => p
        .UseValidation(v =>
        {
            v.AllowedExtensions = new[] { ".pdf", ".docx", ".xlsx" };
            v.MaxFileSizeBytes  = 200 * 1024 * 1024; // 200 MB
        })
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
            ContentType = "application/pdf",
            Metadata    = new Dictionary<string, string>
            {
                ["uploaded-by"] = "api-service",
                ["env"]         = "production"
            }
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
    Path = StoragePath.From("documents", "contract.pdf")
});

if (result.IsSuccess)
    await result.Value!.CopyToAsync(outputStream);
```

### Pre-Authenticated Request (PAR) URL

```csharp
// Download PAR — each call creates a new PAR in OCI
var url = await storage.GetPresignedDownloadUrlAsync(new PresignedUrlRequest
{
    Path      = StoragePath.From("reports", "q4-2024.pdf"),
    ExpiresIn = TimeSpan.FromHours(24)
});

if (url.IsSuccess)
    return Ok(new { downloadUrl = url.Value });
```

### Resumable (multipart) upload

```csharp
var session = await resumable.StartUploadAsync(new ResumableUploadRequest
{
    FileName    = "large-archive.zip",
    ContentType = "application/zip",
    TotalSize   = totalBytes
});

for (int i = 0; i < chunks.Count; i++)
{
    await resumable.UploadChunkAsync(new ResumableChunkRequest
    {
        SessionId  = session.SessionId,
        ChunkIndex = i,
        Data       = chunks[i]
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
| Metadata (set at upload time) | Yes |
| In-place metadata update | No — re-upload required |
| Presigned URLs (via OCI PARs) | Yes |
| Resumable chunked uploads | Yes |
| BucketOverride per request | Yes |
| Polly retry resilience | Yes |

---

## Options reference

| Property | Default | Description |
|---|---|---|
| `Namespace` | — | OCI tenancy namespace (required) |
| `Bucket` | — | Object Storage bucket name (required) |
| `Region` | — | OCI region identifier (e.g. `us-ashburn-1`) |
| `TenancyId` | — | OCID of the tenancy |
| `UserId` | — | OCID of the API user |
| `Fingerprint` | — | API key fingerprint |
| `PrivateKeyPath` | — | Path to the PEM private key file |
| `PrivateKeyContent` | — | PEM private key as a string |
| `ParExpiryMinutes` | `60` | Default expiry for generated PARs |

---

## Documentation

- [OCI Object Storage provider docs](https://vali-blob-docs.netlify.app/docs/providers/oci)
- [Pre-Authenticated Requests (PARs)](https://vali-blob-docs.netlify.app/docs/providers/oci#presigned-urls)
- [Resumable uploads](https://vali-blob-docs.netlify.app/docs/resumable-uploads)
- [Full documentation](https://vali-blob-docs.netlify.app)

---

## Links

- [GitHub Repository](https://github.com/UBF21/Vali-Blob)
- [NuGet Package](https://www.nuget.org/packages/ValiBlob.OCI)

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
