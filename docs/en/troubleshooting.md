# Troubleshooting

Concrete fixes for the most common problems encountered when running ValiBlob in production.

---

## "Upload session not found or expired"

**Symptom:** `UploadChunkAsync` or `CompleteUploadAsync` returns a failed result with the message `Upload session not found or expired`.

**Cause:** One of the following:

- The application process was restarted and the in-memory session store was lost.
- The session exceeded its configured `SessionExpiration` window before the upload completed.
- The load balancer is routing chunk requests to a different instance than the one that created the session.

**Solution:**

Configure a persistent session store backed by Redis so sessions survive restarts and are shared across instances:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithResumableUploads(r => r
        .UseRedisSessionStore(opts =>
        {
            opts.ConnectionString = Environment.GetEnvironmentVariable("REDIS_URL");
            opts.KeyPrefix        = "valiblob:sessions:";
        })
        .SetSessionExpiration(TimeSpan.FromHours(24)));
```

If you cannot use Redis, configure sticky sessions on your load balancer so all requests for a given upload are routed to the same instance.

To increase the session window without changing infrastructure:

```csharp
.SetSessionExpiration(TimeSpan.FromHours(48))
```

---

## GCP presigned URLs return `NotSupported`

**Symptom:** Calling `GetPresignedUploadUrlAsync` or `GetPresignedDownloadUrlAsync` on the GCP provider throws `NotSupportedException` or returns a failed result with `NotSupported`.

**Cause:** GCP presigned URL signing requires a service account private key. When the application runs with Application Default Credentials (ADC) — such as the Compute Engine metadata server or `gcloud auth application-default login` — the signing operation is not available.

**Solution:** Provide an explicit service account credentials file:

```json
{
  "ValiBlob:GCP": {
    "Bucket": "my-bucket",
    "CredentialsPath": "/run/secrets/gcp-service-account.json"
  }
}
```

Or pass the JSON content directly:

```json
{
  "ValiBlob:GCP": {
    "Bucket": "my-bucket",
    "CredentialsJson": "{ \"type\": \"service_account\", ... }"
  }
}
```

The service account needs `roles/storage.objectAdmin` on the bucket and `roles/iam.serviceAccountTokenCreator` on itself. See [Security](security.md) for the full IAM setup.

---

## AWS: `InvalidPart` / ETag mismatch on `CompleteMultipartUpload`

**Symptom:** The final `CompleteUploadAsync` call fails with an `InvalidPart` error or an ETag mismatch.

**Cause:** S3 multipart upload requires that parts are listed in the exact order they were uploaded, paired with the ETag S3 returned for each part. This can go wrong when:

- Parts were uploaded out of order.
- The session was lost and a new session was started, orphaning the original S3 multipart upload.
- The ETag was not stored correctly between chunks.

**Solution:**

ValiBlob stores the part ETags in the session. If a session is lost, the in-progress S3 multipart upload will be orphaned (and will incur storage costs until it expires). Abort orphaned uploads with:

```bash
aws s3api list-multipart-uploads --bucket my-bucket
aws s3api abort-multipart-upload --bucket my-bucket \
  --key "path/to/file" --upload-id "UPLOAD_ID"
```

To prevent accumulation, configure an S3 lifecycle rule to abort incomplete multipart uploads after 7 days:

```json
{
  "Rules": [{
    "ID": "abort-incomplete-mpu",
    "Status": "Enabled",
    "AbortIncompleteMultipartUpload": { "DaysAfterInitiation": 7 }
  }]
}
```

---

## Circuit breaker opens unexpectedly

**Symptom:** Requests start failing immediately with a circuit breaker open error, even though the storage provider is reachable.

**Cause:** The circuit breaker opened because the failure ratio or minimum throughput thresholds were reached. This can happen if the default thresholds are too sensitive for your traffic pattern — for example, a single bulk operation producing several 404s during a listing operation.

**Solution:**

Inspect logs for the reason the circuit opened (look for `CircuitBreakerOpened` events in your telemetry). Then adjust the thresholds:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithResilience(r => r
        .CircuitBreaker(cb =>
        {
            cb.FailureRatio       = 0.5;   // open when 50% of calls fail (default: 0.3)
            cb.MinimumThroughput  = 20;    // require at least 20 calls in the sampling window
            cb.SamplingDuration   = TimeSpan.FromSeconds(30);
            cb.BreakDuration      = TimeSpan.FromSeconds(15);
        }));
```

If 404 responses on list/download operations should not count as failures, configure the circuit breaker to ignore `StorageErrorCode.NotFound`:

```csharp
cb.ShouldHandle = result => result.ErrorCode != StorageErrorCode.NotFound;
```

---

## File not found on download but exists in the bucket

**Symptom:** `DownloadAsync` returns `NotFound`, but you can see the file in the bucket console or CLI.

**Cause:** The file was uploaded with a `BucketOverride` that differs from the bucket used during the download request. Both requests appear to succeed, but they target different buckets.

**Solution:**

Ensure `BucketOverride` is consistent across upload and download for the same file. If you store the resulting path from `UploadAsync`, also store which bucket was used:

```csharp
var uploadResult = await _storage.UploadAsync(new UploadRequest
{
    Path           = path,
    Content        = stream,
    BucketOverride = tenantBucket
});

// Store both the path and the bucket
await _db.SaveFileRecord(new FileRecord
{
    Path   = uploadResult.Value!.Path,
    Bucket = tenantBucket
});

// Later, download using the stored bucket
var downloadResult = await _storage.DownloadAsync(new DownloadRequest
{
    Path           = record.Path,
    BucketOverride = record.Bucket
});
```

---

## Compression: downloaded file is unreadable / garbage bytes

**Symptom:** A file uploaded with compression enabled downloads as garbled binary data instead of readable content.

**Cause:** The file was compressed with GZip before upload (by `CompressionMiddleware`), but the HTTP client or browser is not decompressing it because the `Content-Encoding: gzip` header was not set on the stored object.

**Solution:**

When uploading, set the `ContentEncoding` property on the request so ValiBlob stores the header alongside the object metadata:

```csharp
var request = new UploadRequest
{
    Path            = StoragePath.From("exports", "report.json"),
    Content         = stream,
    ContentType     = "application/json",
    ContentEncoding = "gzip"
};
```

When serving the file to a browser or HTTP client, include the `Content-Encoding: gzip` header in the HTTP response. If you are passing the stream directly to the response, decompress it server-side:

```csharp
using var gzip = new GZipStream(downloadResult.Value!, CompressionMode.Decompress);
await gzip.CopyToAsync(httpContext.Response.Body);
```

---

## Azure: `BlobNotFoundException` after upload appears to succeed

**Symptom:** Upload returns success, but a subsequent download or existence check returns `BlobNotFound`.

**Cause:** The container does not exist. The Azure provider by default assumes the container is already created and will not create it automatically.

**Solution:**

Enable automatic container creation:

```json
{
  "ValiBlob:Azure": {
    "Container": "my-files",
    "ConnectionString": "DefaultEndpointsProtocol=https;...",
    "CreateContainerIfNotExists": true
  }
}
```

Or create the container manually before starting the application:

```bash
az storage container create \
  --name my-files \
  --account-name mystorageaccount \
  --auth-mode login
```

---

## OCI: `SetMetadata` returns `NotSupported`

**Symptom:** Calling `SetMetadataAsync` on the OCI provider returns a `NotSupported` error.

**Cause:** Oracle Cloud Infrastructure Object Storage does not support updating object metadata in-place after the object has been stored. The OCI SDK does not expose a standalone set-metadata operation for existing objects.

**Workaround:**

Re-upload the object with the desired metadata. ValiBlob's OCI provider applies metadata during the `PutObject` call:

```csharp
// Download the existing file
var existing = await _storage.DownloadAsync(new DownloadRequest { Path = path });

// Re-upload with updated metadata
await _storage.UploadAsync(new UploadRequest
{
    Path        = path,
    Content     = existing.Value!,
    ContentType = "application/pdf",
    Metadata    = new Dictionary<string, string>
    {
        ["processed"] = "true",
        ["version"]   = "2"
    }
});
```

---

## Large file upload timing out

**Symptom:** Uploading files larger than a few hundred MB fails with a timeout error.

**Cause:** The default resilience timeout is too short for large file transfers over slower connections.

**Solution:**

Increase the timeout for large file operations:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithResilience(r => r
        .Timeout(TimeSpan.FromMinutes(30)));
```

For files larger than 100 MB, switch to resumable uploads entirely. Resumable uploads split the file into chunks so no single HTTP call needs to transfer the entire payload:

```csharp
// Start session
var session = await _resumable.StartUploadAsync(new ResumableUploadRequest
{
    FileName    = "large-dataset.csv",
    ContentType = "text/csv",
    TotalSize   = fileInfo.Length
});

// Upload chunks
const int chunkSize = 8 * 1024 * 1024; // 8 MB
// ... chunking loop ...

// Complete
await _resumable.CompleteUploadAsync(session.SessionId);
```

See [Resumable Uploads](resumable-uploads.md) for the complete implementation.

---

## Health check always reports `Unhealthy`

**Symptom:** The `/healthz` endpoint reports `Unhealthy` for one or more ValiBlob providers.

**Cause:** The health check performs a lightweight probe against the provider (usually a bucket existence check or a small list operation). Common causes of failure:

- Credentials are wrong or expired.
- The bucket or container does not exist.
- Network connectivity to the cloud endpoint is blocked.
- The IAM role or permission is missing `ListBucket` / `GetBucketLocation`.

**Solution:**

1. Check your application logs for the underlying exception. ValiBlob logs the full exception under the `ValiBlob.HealthChecks` category.

2. Verify credentials independently:

```bash
# AWS
aws s3 ls s3://my-bucket --region us-east-1

# Azure
az storage blob list --container-name my-container --account-name myaccount --auth-mode login

# GCP
gcloud storage ls gs://my-bucket
```

3. Ensure the bucket exists and the service account has at minimum `ListBucket` permission.

4. If the health check is expected to pass even when the bucket is empty, confirm the probe is doing a list operation and not relying on a specific file's existence.

5. If a provider is intentionally optional (for example, only used in certain environments), exclude it from the mandatory health check:

```csharp
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("required")
});
```

And tag your checks accordingly when registering them:

```csharp
builder.Services
    .AddHealthChecks()
    .AddValiBlob("AWS", tags: new[] { "required" })
    .AddValiBlob("GCP", tags: new[] { "optional" });
```
