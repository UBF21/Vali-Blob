# ValiBlob

**Cloud storage for .NET — one API, any provider, zero lock-in.**

ValiBlob is a unified cloud storage abstraction for .NET that lets you swap AWS S3, Azure Blob Storage, Google Cloud Storage, Oracle Cloud Infrastructure, and Supabase Storage without changing a single line of business code. Upload, download, list, copy, move, and delete files through a consistent interface, and add validation, compression, encryption, and resilience via a composable middleware pipeline.

---

## Why ValiBlob?

### The vendor lock-in problem

Every major cloud provider ships its own SDK with its own vocabulary:

| Provider | SDK | Upload method |
|---|---|---|
| AWS S3 | AWSSDK.S3 | `PutObjectAsync` |
| Azure Blob | Azure.Storage.Blobs | `UploadAsync` |
| GCP Storage | Google.Cloud.Storage.V1 | `UploadObjectAsync` |
| OCI Object Storage | OCI.DotNetSDK.Objectstorage | `PutObjectAsync` |
| Supabase | REST/HTTP | `POST /object/{bucket}` |

Migrating between providers, running tests without cloud credentials, or supporting multiple providers in the same application requires deep refactoring.

### The ValiBlob solution

ValiBlob places a single `IStorageProvider` interface in front of all providers. Your application code calls `UploadAsync`, `DownloadAsync`, `DeleteAsync` — the same method names, the same return types, the same error model — regardless of which cloud sits underneath.

```
Your Code  →  IStorageProvider  →  [ AWS | Azure | GCP | OCI | Supabase ]
```

Switch providers by changing two lines in `appsettings.json`. No service references, no SDK imports, no domain model changes.

---

## Feature highlights

| Feature | Description |
|---|---|
| Unified API | One `IStorageProvider` interface for all clouds |
| Middleware pipeline | Composable pre-upload hooks (validation → compression → encryption) |
| Built-in validation | Extension allowlist/blocklist, max file size, content-type filtering |
| Client-side encryption | AES-256-CBC with per-file random IV, transparent to callers |
| GZip compression | Automatic compression for text/JSON/XML content types |
| Resilience via Polly | Retry with exponential back-off + jitter, circuit breaker, timeout |
| Event hooks | `OnUploadCompleted`, `OnUploadFailed`, `OnDownloadCompleted`, `OnDeleteCompleted` |
| OpenTelemetry | Metrics (counters, histograms) and distributed tracing activities |
| Health checks | ASP.NET Core health check integration per provider |
| Batch operations | `DeleteManyAsync`, `ListAllAsync` (streaming), `DeleteFolderAsync` |
| Remote upload | `UploadFromUrlAsync` — copy a remote URL directly to storage, no server proxy |
| Multipart upload | Automatic multipart for large files (AWS S3) |
| **Resumable uploads** | `IResumableUploadProvider` — chunked uploads with pause/resume; AWS, Azure, Supabase (TUS), GCP, OCI |
| Presigned URLs | Temporary signed upload/download URLs (AWS, Azure, Supabase) |
| BucketOverride | Per-request bucket selection for multi-tenant architectures |
| Local filesystem | `ValiBlob.Local` — full provider on disk, ideal for development and Docker Compose |
| In-memory testing | `ValiBlob.Testing` — full provider backed by `ConcurrentDictionary`, no cloud needed |

---

## Supported providers

| Provider | Package | Key |
|---|---|---|
| AWS S3 / MinIO | `ValiBlob.AWS` | `"AWS"` |
| Azure Blob Storage | `ValiBlob.Azure` | `"Azure"` |
| Google Cloud Storage | `ValiBlob.GCP` | `"GCP"` |
| Oracle Cloud (OCI) | `ValiBlob.OCI` | `"OCI"` |
| Supabase Storage | `ValiBlob.Supabase` | `"Supabase"` |
| Local Filesystem | `ValiBlob.Local` | `"Local"` |
| In-Memory (tests) | `ValiBlob.Testing` | `"InMemory"` |

---

## Quick install

```bash
# Core (required)
dotnet add package ValiBlob.Core

# Pick one or more providers
dotnet add package ValiBlob.AWS
dotnet add package ValiBlob.Azure
dotnet add package ValiBlob.GCP
dotnet add package ValiBlob.OCI
dotnet add package ValiBlob.Supabase
dotnet add package ValiBlob.Local

# Optional extras
dotnet add package ValiBlob.HealthChecks
dotnet add package ValiBlob.Testing   # test projects only
```

---

## 5-minute quickstart

### 1. Install packages

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.AWS
```

### 2. Configure `appsettings.json`

```json
{
  "ValiBlob": {
    "DefaultProvider": "AWS"
  },
  "ValiBlob:AWS": {
    "Bucket": "my-app-files",
    "Region": "us-east-1",
    "AccessKeyId": "AKIAIOSFODNN7EXAMPLE",
    "SecretAccessKey": "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
  }
}
```

### 3. Register in `Program.cs`

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.AWS.Extensions;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseAWS()
    .WithPipeline(p => p
        .UseValidation(v =>
        {
            v.AllowedExtensions = new[] { ".jpg", ".png", ".pdf" };
            v.MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
        }));
```

### 4. Upload a file

```csharp
public class FileService
{
    private readonly IStorageProvider _storage;

    public FileService(IStorageProvider storage) => _storage = storage;

    public async Task<string> UploadAvatarAsync(Stream imageStream, string userId)
    {
        var path = StoragePath.From("avatars", userId, "profile.jpg");

        var request = new UploadRequest
        {
            Path = path,
            Content = imageStream,
            ContentType = "image/jpeg"
        };

        var result = await _storage.UploadAsync(request);

        if (!result.IsSuccess)
            throw new Exception($"Upload failed: {result.ErrorMessage}");

        return result.Value!.Url ?? path.ToString();
    }
}
```

### 5. Download a file

```csharp
var result = await _storage.DownloadAsync(new DownloadRequest
{
    Path = StoragePath.From("avatars", userId, "profile.jpg")
});

if (result.IsSuccess)
{
    using var fileStream = File.Create("profile.jpg");
    await result.Value!.CopyToAsync(fileStream);
}
```

### 6. Delete a file

```csharp
var result = await _storage.DeleteAsync("avatars/user-123/profile.jpg");
if (!result.IsSuccess)
    Console.WriteLine($"Delete failed: {result.ErrorMessage}");
```

---

## Package architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Your Application                        │
│              IStorageProvider / IStorageFactory              │
└────────────────────────┬────────────────────────────────────┘
                         │
              ┌──────────▼──────────┐
              │   ValiBlob.Core      │
              │  ─────────────────  │
              │  Pipeline Builder   │
              │  Resilience (Polly) │
              │  Event Dispatcher   │
              │  Telemetry          │
              └──────────┬──────────┘
                         │
         ┌───────────────┼────────────────────────────┐
         │               │               │             │           │
    ┌────▼────┐   ┌──────▼───┐   ┌──────▼──┐  ┌──────▼──┐  ┌────▼──────┐
    │ AWS S3  │   │  Azure   │   │  GCP    │  │  OCI    │  │ Supabase  │
    │ / MinIO │   │  Blob    │   │ Storage │  │ Object  │  │ Storage   │
    └─────────┘   └──────────┘   └─────────┘  └─────────┘  └───────────┘

              ┌──────────────────────────────────┐
              │     Supporting Packages           │
              │  ValiBlob.HealthChecks            │
              │  ValiBlob.Testing (InMemory)      │
              └──────────────────────────────────┘
```

---

## .NET compatibility

| Target Framework | Supported |
|---|---|
| `netstandard2.0` | Yes |
| `netstandard2.1` | Yes |
| `net6.0` | Yes |
| `net7.0` | Yes |
| `net8.0` | Yes |
| `net9.0` | Yes |

---

## Documentation

| Document | Description |
|---|---|
| [Getting Started](getting-started.md) | Installation, configuration, first operations |
| [StoragePath](storage-path.md) | Typed path model — creation, operators, properties |
| [AWS S3 / MinIO](providers/aws.md) | AWS provider configuration and MinIO compatibility |
| [Azure Blob Storage](providers/azure.md) | Azure provider configuration and SAS tokens |
| [Google Cloud Storage](providers/gcp.md) | GCP provider and credential options |
| [Oracle Cloud (OCI)](providers/oci.md) | OCI Object Storage configuration |
| [Supabase Storage](providers/supabase.md) | Supabase provider and public/private buckets |
| [Local Filesystem](providers/local.md) | Local provider for development and testing |
| [Pipeline & Middleware](pipeline.md) | Validation, compression, encryption, custom middleware |
| [Content-Type Detection](content-type-detection.md) | Magic-byte inspection to detect real MIME types |
| [Deduplication](deduplication.md) | SHA-256 content hashing and duplicate detection |
| [Virus Scanning](virus-scanning.md) | Pluggable antivirus scanning via `IVirusScanner` |
| [Storage Quotas](quota.md) | Per-scope usage limits and quota enforcement |
| [Conflict Resolution](conflict-resolution.md) | Overwrite, Rename, or Fail on path conflicts |
| [Resilience](resilience.md) | Retry, circuit breaker, timeout via Polly |
| [Event Hooks](event-hooks.md) | Upload/download/delete lifecycle events |
| [Telemetry](telemetry.md) | OpenTelemetry metrics, traces, Prometheus, App Insights |
| [Health Checks](health-checks.md) | ASP.NET Core health check integration |
| [Multi-Tenant](multi-tenant.md) | BucketOverride, per-request isolation |
| [Batch Operations](batch-operations.md) | DeleteMany, ListAll streaming, folder operations |
| [Resumable Uploads](resumable-uploads.md) | Chunked uploads, TUS protocol, session store, provider matrix |
| [Session Stores](session-stores.md) | Redis and EF Core session stores for resumable uploads |
| [Storage Migration](migration.md) | Cross-provider file migration with dry run and progress reporting |
| [CDN Integration](cdn.md) | Map storage paths to CDN URLs and invalidate cache |
| [Image Processing](image-processing.md) | Resize, reformat, and thumbnail generation via ImageSharp |
| [Storage Path Helpers](storage-path-helpers.md) | Date prefixes, hash suffixes, random suffixes, sanitization |
| [Testing](testing.md) | InMemoryStorageProvider, unit tests, Testcontainers |
| [Security](security.md) | Path traversal prevention, credentials, encryption, least privilege |
| [Encryption & Decryption](encryption-decryption.md) | AES-256-CBC full round-trip, key management, encryption + compression |
| [Troubleshooting](troubleshooting.md) | Common errors and their fixes |
| [API Reference](api-reference.md) | Full interface and model reference |

---

## License

ValiBlob is released under the MIT License.
