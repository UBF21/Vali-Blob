# Google Cloud Storage Provider

The `ValiBlob.GCP` package wraps the `Google.Cloud.Storage.V1` SDK and provides full `IStorageProvider` support for Google Cloud Storage (GCS).

---

## Installation

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.GCP
```

---

## Authentication options

### Application Default Credentials (ADC)

When neither `CredentialsPath` nor `CredentialsJson` is set, the provider calls `StorageClient.Create()` which uses Application Default Credentials. ADC resolves credentials from the following sources in order:

1. `GOOGLE_APPLICATION_CREDENTIALS` environment variable pointing to a service account JSON file
2. gcloud CLI credentials (`gcloud auth application-default login`)
3. Google Compute Engine / Cloud Run / GKE metadata server

```json
{
  "ValiBlob:GCP": {
    "Bucket": "my-gcp-bucket",
    "ProjectId": "my-gcp-project"
  }
}
```

This is the recommended approach for production deployments on Google Cloud infrastructure.

### Service account JSON file path

Point to a downloaded service account key file:

```json
{
  "ValiBlob:GCP": {
    "Bucket": "my-gcp-bucket",
    "ProjectId": "my-gcp-project",
    "CredentialsPath": "/etc/gcp/service-account.json"
  }
}
```

```csharp
builder.Services
    .AddValiBlob()
    .UseGCP(opts =>
    {
        opts.Bucket = "my-gcp-bucket";
        opts.ProjectId = "my-gcp-project";
        opts.CredentialsPath = "/run/secrets/gcp-sa.json";
    });
```

### Inline JSON string

Embed the service account JSON directly as a configuration value. This is useful in environments where mounting files is inconvenient (e.g., some Kubernetes setups where the JSON is stored in a secret and injected as an environment variable).

```json
{
  "ValiBlob:GCP": {
    "Bucket": "my-gcp-bucket",
    "ProjectId": "my-gcp-project",
    "CredentialsJson": "{\"type\":\"service_account\",\"project_id\":\"my-gcp-project\",...}"
  }
}
```

> **⚠️ Warning:** Embedding service account JSON in application settings exposes credentials to anyone with access to the configuration source. Prefer Kubernetes Secrets, GCP Secret Manager, or ADC for production deployments.

---

## Full `GCPStorageOptions` reference

| Property | Type | Default | Description |
|---|---|---|---|
| `Bucket` | `string` | `""` | Default GCS bucket name |
| `ProjectId` | `string` | `""` | GCP project ID |
| `CredentialsPath` | `string?` | `null` | Absolute path to a service account JSON key file |
| `CredentialsJson` | `string?` | `null` | Inline service account JSON string |
| `CdnBaseUrl` | `string?` | `null` | CDN prefix substituted in `GetUrlAsync` responses |

Configuration section: `ValiBlob:GCP`

When both `CredentialsPath` and `CredentialsJson` are set, `CredentialsPath` takes precedence.

---

## `appsettings.json` example

```json
{
  "ValiBlob": {
    "DefaultProvider": "GCP"
  },
  "ValiBlob:GCP": {
    "Bucket": "my-production-assets",
    "ProjectId": "my-gcp-project-12345",
    "CredentialsPath": "/etc/gcp/service-account.json",
    "CdnBaseUrl": "https://assets.example.com"
  }
}
```

---

## Code-only configuration

```csharp
builder.Services
    .AddValiBlob(o => o.DefaultProvider = "GCP")
    .UseGCP(opts =>
    {
        opts.Bucket = "my-production-assets";
        opts.ProjectId = "my-gcp-project-12345";
        // Using ADC — no credentials needed in code
    });
```

---

## CDN configuration

Set `CdnBaseUrl` to replace the default GCS object URL with a custom CDN domain in `GetUrlAsync` responses.

```json
"CdnBaseUrl": "https://assets.example.com"
```

```csharp
var result = await _storage.GetUrlAsync("images/hero-banner.jpg");
// Returns: "https://assets.example.com/images/hero-banner.jpg"
// Without CDN would return: "https://storage.googleapis.com/my-bucket/images/hero-banner.jpg"
```

For Cloud CDN integration, configure the CDN origin to point to the GCS bucket endpoint with the appropriate IAM permissions.

---

## BucketOverride

Use `BucketOverride` to target a different GCS bucket for a specific operation:

```csharp
var result = await _storage.UploadAsync(new UploadRequest
{
    Path = StoragePath.From("exports", "data-2024-q4.csv"),
    Content = csvStream,
    ContentType = "text/csv",
    BucketOverride = "my-archive-bucket"
});
```

---

## Presigned URLs (signed URLs)

GCP Storage supports Signed URLs via the V4 signing process. The GCP provider implements `IPresignedUrlProvider` when the `StorageClient` was created with a `ServiceAccountCredential` (i.e., when `CredentialsPath` or `CredentialsJson` is provided).

> **⚠️ Warning:** Signed URLs cannot be generated using Application Default Credentials from a Compute Engine service account without additional configuration. If you need presigned URLs in a GCE/Cloud Run environment, provide an explicit service account JSON with `CredentialsPath`.

```csharp
if (_storage is IPresignedUrlProvider signedUrl)
{
    var result = await signedUrl.GetPresignedDownloadUrlAsync(
        "reports/annual-2024.pdf",
        expiration: TimeSpan.FromHours(24));

    Console.WriteLine(result.Value);
}
```

---

## Limitations

- `SetMetadataAsync` updates object metadata using `StorageClient.PatchObjectAsync`. GCS supports in-place metadata patching without re-uploading.
- GCS bucket names are globally unique across all Google Cloud projects.
- Object names (paths) are limited to 1024 bytes encoded as UTF-8.
- Maximum individual object size is 5 TiB.
- Composite objects (GCS equivalent of S3 multipart) are used for large uploads automatically via the `Google.Cloud.Storage.V1` upload stream.
