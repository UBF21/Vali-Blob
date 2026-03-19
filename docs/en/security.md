# Security Guide

This document covers the security features built into ValiBlob and the practices you should follow when integrating it into a production application.

---

## Path traversal prevention

ValiBlob's `ValidationMiddleware` rejects any upload path that contains `..` segments before the request reaches the provider. This prevents an attacker from escaping the intended storage prefix and writing to arbitrary locations.

Blocked paths:

```
../secrets/config
documents/../../admin/keys.pem
uploads/../../../etc/passwd
```

Allowed paths:

```
documents/invoices/2024/inv-001.pdf
avatars/user-123/profile.jpg
reports/monthly/march.xlsx
```

`StoragePath` normalises the path at construction time, removing duplicate slashes and resolving `.` segments. Always build paths with `StoragePath.From(...)` rather than raw strings:

```csharp
// Good — normalised, validated
var path = StoragePath.From("documents", userId, "report.pdf");

// Risky — raw string bypasses normalisation helpers
var path = new StoragePath($"documents/{userId}/report.pdf");
```

If you need to accept user-supplied file names, sanitise them before building the path:

```csharp
var safeName = Path.GetFileName(userSuppliedName); // strips directory components
var path = StoragePath.From("uploads", tenantId, safeName);
```

---

## Credential management

Never hardcode cloud credentials in source code or `appsettings.json` committed to version control.

### Environment variables (recommended for containers)

```json
{
  "ValiBlob:AWS": {
    "Bucket": "my-bucket",
    "Region": "us-east-1",
    "AccessKeyId": "",
    "SecretAccessKey": ""
  }
}
```

```bash
ValiBlob__AWS__AccessKeyId=AKIA...
ValiBlob__AWS__SecretAccessKey=wJalr...
```

### ASP.NET Core User Secrets (local development)

```bash
dotnet user-secrets set "ValiBlob:AWS:AccessKeyId" "AKIA..."
dotnet user-secrets set "ValiBlob:AWS:SecretAccessKey" "wJalr..."
```

### Azure Key Vault (production)

```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri("https://my-vault.vault.azure.net/"),
    new DefaultAzureCredential());
```

Store secrets as `ValiBlob--AWS--AccessKeyId` (double-dash maps to the colon separator).

### AWS Secrets Manager (production)

```csharp
builder.Configuration.AddSecretsManager(region: RegionEndpoint.USEast1, configurator: opts =>
{
    opts.SecretFilter = entry => entry.Name.StartsWith("valiblob/");
    opts.KeyGenerator = (entry, key) => key.Replace("valiblob/", "").Replace("/", ":");
});
```

On EC2 / ECS / Lambda, prefer IAM roles so no static credentials are needed at all — leave `AccessKeyId` and `SecretAccessKey` blank and AWS SDK will pick up the instance profile automatically.

---

## Client-side encryption

ValiBlob's `EncryptionMiddleware` encrypts file content with AES-256-CBC before it leaves your application. Even if a bucket is misconfigured or credentials are compromised, the stored bytes are unreadable without the key.

### Configuration

```csharp
builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseAWS()
    .WithPipeline(p => p
        .UseEncryption(e =>
        {
            e.Key = Convert.FromBase64String(Environment.GetEnvironmentVariable("STORAGE_ENC_KEY")!);
            e.IV  = Convert.FromBase64String(Environment.GetEnvironmentVariable("STORAGE_ENC_IV")!);
        }));
```

### Generating a secure key and IV

```csharp
using System.Security.Cryptography;

using var aes = Aes.Create();
aes.KeySize = 256;
aes.GenerateKey();
aes.GenerateIV();

Console.WriteLine("Key: " + Convert.ToBase64String(aes.Key));
Console.WriteLine("IV:  " + Convert.ToBase64String(aes.IV));
```

> **Warning:** A fixed IV reuses the same initialisation vector for every file. This is acceptable for encrypted-at-rest data where files are independent, but it reduces confidentiality guarantees compared to a random per-file IV. If your threat model requires per-file IVs, implement a custom `IEncryptionMiddleware` that prepends the random IV to the ciphertext and strips it on download.

### Key rotation

When rotating the encryption key, existing files must be re-encrypted. A safe rotation procedure:

1. Download the file with the old key configured.
2. Re-upload the file with the new key configured.
3. Delete the old file after confirming the re-upload.

Never delete the old key until all files encrypted with it have been rotated.

---

## Chunk checksum validation

When using resumable uploads, enable checksum validation to detect data corruption in transit:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithResumableUploads(r => r
        .EnableChecksumValidation());
```

For each chunk, provide the expected MD5 hash:

```csharp
var chunkRequest = new ResumableChunkRequest
{
    SessionId  = sessionId,
    ChunkIndex = 0,
    Data       = chunkStream,
    ExpectedMd5 = ComputeMd5(chunkBytes) // your helper
};

var result = await _resumable.UploadChunkAsync(chunkRequest);
```

If the received chunk's MD5 does not match `ExpectedMd5`, ValiBlob returns a failed `StorageResult` with an appropriate error message. Retry the chunk rather than proceeding — a corrupt chunk will cause the assembled file to be invalid.

---

## Presigned URL security

Presigned URLs grant temporary access to a specific object without requiring the caller to hold cloud credentials. Follow these guidelines:

- **Set short expirations.** For most use cases, 5–15 minutes is sufficient. Never issue presigned URLs without an expiration.
- **Generate per-request.** Do not cache and reuse presigned URLs. Each operation should produce a fresh URL for the intended recipient.
- **Distinguish upload from download URLs.** An upload presigned URL should only be used to write one specific object. A download URL should only be used to read. Do not use the same URL for both directions.
- **Audit URL issuance.** Log who requested a presigned URL, for which object, and when. This allows you to detect abuse patterns.

```csharp
// Upload presigned URL — expires in 10 minutes
var uploadUrl = await _presigned.GetPresignedUploadUrlAsync(new PresignedUrlRequest
{
    Path       = StoragePath.From("uploads", Guid.NewGuid().ToString()),
    Expiration = TimeSpan.FromMinutes(10)
});

// Download presigned URL — expires in 5 minutes
var downloadUrl = await _presigned.GetPresignedDownloadUrlAsync(new PresignedUrlRequest
{
    Path       = StoragePath.From("documents", "invoice-001.pdf"),
    Expiration = TimeSpan.FromMinutes(5)
});
```

---

## Bucket isolation

Use `BucketOverride` to enforce per-tenant isolation at the request level:

```csharp
var request = new UploadRequest
{
    Path           = StoragePath.From("reports", fileName),
    Content        = stream,
    ContentType    = "application/pdf",
    BucketOverride = $"tenant-{tenantId}-files"
};
```

For strict multi-tenant isolation, derive the bucket name from a verified tenant identifier — never from user input directly. Validate that the resolved bucket name is in your allowlist before issuing the request:

```csharp
private string ResolveBucket(string tenantId)
{
    if (!_allowedTenants.Contains(tenantId))
        throw new UnauthorizedAccessException($"Unknown tenant: {tenantId}");

    return $"tenant-{tenantId}-files";
}
```

See [Multi-Tenant](multi-tenant.md) for complete isolation strategies.

---

## Principle of least privilege

Grant your application only the permissions it needs. Avoid using root or admin credentials.

### AWS IAM — minimal S3 policy

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject",
        "s3:DeleteObject",
        "s3:ListBucket"
      ],
      "Resource": [
        "arn:aws:s3:::my-app-bucket",
        "arn:aws:s3:::my-app-bucket/*"
      ]
    }
  ]
}
```

If presigned URLs are needed, also add `s3:GetObjectAttributes` and remove the explicit object ARN restriction for the `ListBucket` action.

### Azure RBAC

Assign the `Storage Blob Data Contributor` role scoped to the specific storage container, not the entire storage account:

```
Scope: /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Storage/storageAccounts/{account}/blobServices/default/containers/{container}
Role:  Storage Blob Data Contributor
```

For read-only workloads, use `Storage Blob Data Reader` instead.

### GCP IAM

Assign `roles/storage.objectAdmin` scoped to the specific bucket, not the project:

```bash
gcloud storage buckets add-iam-policy-binding gs://my-app-bucket \
  --member="serviceAccount:my-app@my-project.iam.gserviceaccount.com" \
  --role="roles/storage.objectAdmin"
```

For presigned URL generation, the service account also needs `roles/iam.serviceAccountTokenCreator` on itself.

---

## Rate limiting and abuse prevention

ValiBlob does not implement rate limiting — this belongs at your API layer, before ValiBlob is called. Without rate limiting, an attacker can exhaust your cloud storage egress quota, incur large bills, or saturate your network.

Recommended approach with ASP.NET Core:

```csharp
builder.Services.AddRateLimiter(opts =>
{
    opts.AddFixedWindowLimiter("upload", o =>
    {
        o.Window           = TimeSpan.FromMinutes(1);
        o.PermitLimit      = 20;
        o.QueueLimit       = 0;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

// In your endpoint
app.MapPost("/files", UploadHandler)
   .RequireRateLimiting("upload");
```

For resumable upload endpoints, apply rate limiting both to the session creation endpoint and to each chunk upload endpoint separately.
