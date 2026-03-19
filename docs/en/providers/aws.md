# AWS S3 Provider

The `ValiBlob.AWS` package wraps the official `AWSSDK.S3` SDK and provides full `IStorageProvider` support for Amazon S3 and S3-compatible services such as MinIO.

---

## Installation

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.AWS
```

---

## Authentication options

### IAM Role (recommended for EC2 / ECS / Lambda)

Set `UseIAMRole: true`. No access key is needed; credentials are retrieved from the EC2 instance metadata service or ECS task role automatically.

```json
{
  "ValiBlob:AWS": {
    "Bucket": "my-app-files",
    "Region": "us-east-1",
    "UseIAMRole": true
  }
}
```

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS();
```

### Access Key and Secret (local development / CI)

```json
{
  "ValiBlob:AWS": {
    "Bucket": "my-app-files",
    "Region": "us-east-1",
    "UseIAMRole": false,
    "AccessKeyId": "AKIAIOSFODNN7EXAMPLE",
    "SecretAccessKey": "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
  }
}
```

> **⚠️ Warning:** Never commit access keys to source control. Use `dotnet user-secrets` locally, or environment variables in CI pipelines.

### Environment variables

If `UseIAMRole` is `false` and `AccessKeyId` / `SecretAccessKey` are not set in configuration, the underlying AWS SDK will fall back to its default credential chain, which includes:

1. `AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY` environment variables
2. `~/.aws/credentials` shared credentials file
3. EC2 instance metadata / ECS task role

---

## Full `AWSS3Options` reference

| Property | Type | Default | Description |
|---|---|---|---|
| `Bucket` | `string` | `""` | Default S3 bucket name |
| `Region` | `string` | `"us-east-1"` | AWS region system name |
| `AccessKeyId` | `string?` | `null` | AWS access key ID |
| `SecretAccessKey` | `string?` | `null` | AWS secret access key |
| `UseIAMRole` | `bool` | `false` | Use IAM role / instance profile instead of explicit keys |
| `ServiceUrl` | `string?` | `null` | Custom endpoint URL — required for MinIO |
| `ForcePathStyle` | `bool` | `false` | Force path-style addressing — required for MinIO |
| `CdnBaseUrl` | `string?` | `null` | CDN prefix substituted in `GetUrlAsync` responses |
| `MultipartThresholdMb` | `int` | `100` | Files larger than this are uploaded via multipart |
| `MultipartChunkSizeMb` | `int` | `8` | Size of each part in a multipart upload |

Configuration section: `ValiBlob:AWS`

---

## `appsettings.json` example

```json
{
  "ValiBlob": {
    "DefaultProvider": "AWS"
  },
  "ValiBlob:AWS": {
    "Bucket": "my-app-files",
    "Region": "sa-east-1",
    "UseIAMRole": false,
    "AccessKeyId": "AKIAIOSFODNN7EXAMPLE",
    "SecretAccessKey": "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
    "CdnBaseUrl": "https://d1234567890.cloudfront.net",
    "MultipartThresholdMb": 100,
    "MultipartChunkSizeMb": 8
  }
}
```

---

## Code-only configuration

```csharp
builder.Services
    .AddValiBlob(o => o.DefaultProvider = "AWS")
    .UseAWS(opts =>
    {
        opts.Bucket = "my-app-files";
        opts.Region = "us-east-1";
        opts.AccessKeyId = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        opts.SecretAccessKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        opts.CdnBaseUrl = "https://d1234567890.cloudfront.net";
        opts.MultipartThresholdMb = 50;
        opts.MultipartChunkSizeMb = 16;
    });
```

---

## MinIO compatibility (self-hosted S3)

MinIO is S3-compatible but requires two settings:

- `ServiceUrl` — the endpoint URL of your MinIO instance
- `ForcePathStyle` — must be `true` (MinIO does not support virtual-hosted-style addressing)

Use the dedicated `UseMinIO` extension which sets `ForcePathStyle` automatically:

```csharp
builder.Services
    .AddValiBlob(o => o.DefaultProvider = "AWS")
    .UseMinIO(opts =>
    {
        opts.Bucket = "dev-files";
        opts.Region = "us-east-1";   // MinIO ignores the region but the SDK requires a value
        opts.ServiceUrl = "http://localhost:9000";
        opts.AccessKeyId = "minioadmin";
        opts.SecretAccessKey = "minioadmin";
    });
```

Or via `appsettings.json`:

```json
{
  "ValiBlob:AWS": {
    "Bucket": "dev-files",
    "Region": "us-east-1",
    "ServiceUrl": "http://localhost:9000",
    "ForcePathStyle": true,
    "AccessKeyId": "minioadmin",
    "SecretAccessKey": "minioadmin"
  }
}
```

---

## Multipart upload

S3 requires multipart upload for objects larger than 5 GB, and recommends it for anything above a few hundred MB for better throughput and resumability.

ValiBlob triggers multipart upload automatically when the file content length exceeds `MultipartThresholdMb` (default: 100 MB). The upload is split into chunks of `MultipartChunkSizeMb` (default: 8 MB) and assembled by S3.

You can also force multipart upload for a specific request regardless of size:

```csharp
var request = new UploadRequest
{
    Path = StoragePath.From("videos", "lecture.mp4"),
    Content = videoStream,
    ContentType = "video/mp4",
    Options = new UploadOptions
    {
        UseMultipart = true,
        ChunkSizeMb = 16
    }
};
```

---

## Presigned URLs

The AWS provider implements `IPresignedUrlProvider`. Cast or resolve the provider as `IPresignedUrlProvider` to generate temporary signed URLs.

```csharp
public class ShareService
{
    private readonly IStorageProvider _storage;

    public ShareService(IStorageProvider storage) => _storage = storage;

    public async Task<string> GetTemporaryDownloadLinkAsync(string path)
    {
        if (_storage is not IPresignedUrlProvider presigned)
            throw new NotSupportedException("Provider does not support presigned URLs.");

        var result = await presigned.GetPresignedDownloadUrlAsync(
            path,
            expiration: TimeSpan.FromHours(1));

        if (!result.IsSuccess)
            throw new Exception(result.ErrorMessage);

        return result.Value!;
    }

    public async Task<string> GetPresignedUploadLinkAsync(string path)
    {
        if (_storage is not IPresignedUrlProvider presigned)
            throw new NotSupportedException();

        var result = await presigned.GetPresignedUploadUrlAsync(
            path,
            expiration: TimeSpan.FromMinutes(15));

        return result.Value!;
    }
}
```

---

## BucketOverride (multi-tenant)

Supply `BucketOverride` on `UploadRequest` or `DownloadRequest` to target a different bucket for that single operation. The configured default bucket is ignored.

```csharp
// Upload to tenant-specific bucket
var result = await _storage.UploadAsync(new UploadRequest
{
    Path = StoragePath.From("documents", "contract.pdf"),
    Content = stream,
    ContentType = "application/pdf",
    BucketOverride = $"tenant-{tenantId}"
});

// Download from tenant-specific bucket
var download = await _storage.DownloadAsync(new DownloadRequest
{
    Path = StoragePath.From("documents", "contract.pdf"),
    BucketOverride = $"tenant-{tenantId}"
});
```

See [Multi-Tenant](../multi-tenant.md) for a complete multi-tenant architecture guide.

---

## CDN configuration

When `CdnBaseUrl` is set, `GetUrlAsync` returns the CDN URL instead of the S3 endpoint URL. The S3 object key is appended to the base URL.

```json
"CdnBaseUrl": "https://d1a2b3c4d5e6f7.cloudfront.net"
```

```csharp
var result = await _storage.GetUrlAsync("images/photo.jpg");
// Returns: "https://d1a2b3c4d5e6f7.cloudfront.net/images/photo.jpg"
```

---

## Limitations

- `SetMetadataAsync` requires re-uploading the object on S3 because S3 metadata is immutable after upload. The provider handles this transparently by performing a server-side copy with updated metadata, but this doubles the S3 API cost for metadata-only updates.
- Presigned URL expiration maximum on AWS is 7 days when using temporary credentials (IAM role), or up to 7 days with long-term access keys.
- `UploadFromUrlAsync` is performed server-side by the provider by fetching the remote URL and streaming it to S3 — it does not use S3's native `CopyObject` for cross-bucket copies from arbitrary URLs.
