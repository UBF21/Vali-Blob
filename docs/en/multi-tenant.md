# Multi-Tenant Storage

Multi-tenant applications commonly need to store files in separate, isolated buckets or containers for each tenant — so one tenant's files are never visible to another. ValiBlob supports this pattern natively via the `BucketOverride` property available on every operation request.

---

## The multi-tenant problem

With a single shared bucket, all tenants' files exist in the same namespace. Even with path prefixes (`tenant-A/file.pdf`), you risk:

- Misconfigured policies granting cross-tenant access
- Accidental `ListAllAsync` leaking another tenant's file paths
- Inability to independently delete or archive a single tenant's data

Separate buckets per tenant eliminate all of these risks with strong cloud-provider-level isolation.

---

## Using `BucketOverride` per request

Every `UploadRequest` and `DownloadRequest` accepts a `BucketOverride` string. When set, the provider uses that bucket instead of the one configured in options — for that single operation only.

```csharp
// Upload to tenant-specific bucket
await _storage.UploadAsync(new UploadRequest
{
    Path = StoragePath.From("documents", "contract.pdf"),
    Content = pdfStream,
    ContentType = "application/pdf",
    BucketOverride = $"tenant-{tenantId}"
});

// Download from tenant-specific bucket
await _storage.DownloadAsync(new DownloadRequest
{
    Path = StoragePath.From("documents", "contract.pdf"),
    BucketOverride = $"tenant-{tenantId}"
});
```

`DeleteAsync`, `ExistsAsync`, `CopyAsync`, `GetMetadataAsync`, `ListFilesAsync`, and all other operations that accept a path or paths also respect the underlying provider's bucket routing, though the bucket override is specified at the provider level for operations that don't have a dedicated request object.

---

## Resolving the bucket dynamically — factory pattern

For larger applications, abstract bucket resolution behind a dedicated service:

```csharp
public interface ITenantStorageService
{
    Task<StorageResult<UploadResult>> UploadAsync(
        string tenantId, StoragePath path, Stream content, string contentType,
        CancellationToken cancellationToken = default);

    Task<StorageResult<Stream>> DownloadAsync(
        string tenantId, StoragePath path,
        CancellationToken cancellationToken = default);

    Task<StorageResult> DeleteAsync(
        string tenantId, string path,
        CancellationToken cancellationToken = default);
}

public sealed class TenantStorageService : ITenantStorageService
{
    private readonly IStorageProvider _storage;
    private readonly ITenantRepository _tenants;

    public TenantStorageService(IStorageProvider storage, ITenantRepository tenants)
    {
        _storage = storage;
        _tenants = tenants;
    }

    private async Task<string> ResolveBucketAsync(string tenantId)
    {
        var tenant = await _tenants.GetByIdAsync(tenantId)
            ?? throw new KeyNotFoundException($"Tenant not found: {tenantId}");

        return tenant.StorageBucket;  // e.g., "tenant-acme-corp-prod"
    }

    public async Task<StorageResult<UploadResult>> UploadAsync(
        string tenantId, StoragePath path, Stream content, string contentType,
        CancellationToken cancellationToken = default)
    {
        var bucket = await ResolveBucketAsync(tenantId);

        return await _storage.UploadAsync(new UploadRequest
        {
            Path = path,
            Content = content,
            ContentType = contentType,
            BucketOverride = bucket
        }, cancellationToken: cancellationToken);
    }

    public async Task<StorageResult<Stream>> DownloadAsync(
        string tenantId, StoragePath path,
        CancellationToken cancellationToken = default)
    {
        var bucket = await ResolveBucketAsync(tenantId);

        return await _storage.DownloadAsync(new DownloadRequest
        {
            Path = path,
            BucketOverride = bucket
        }, cancellationToken);
    }

    public async Task<StorageResult> DeleteAsync(
        string tenantId, string path,
        CancellationToken cancellationToken = default)
    {
        var bucket = await ResolveBucketAsync(tenantId);
        // Provider-level delete uses the default bucket — pass override via the operation
        return await _storage.DeleteAsync(path, cancellationToken);
        // Note: for delete, route via UploadRequest pattern or use a provider-specific override
    }
}
```

---

## Tenant isolation strategies

### Strategy 1: One bucket per tenant (strongest isolation)

Each tenant has a dedicated bucket: `tenant-{tenantId}`.

- Maximum isolation — independent IAM policies, independent billing, independent deletion
- Higher bucket management overhead (bucket creation on tenant onboarding)
- Most cloud providers have soft limits on the number of buckets (AWS: 100 default, Azure: unlimited containers, GCP: no hard limit per project)

```csharp
var bucket = $"tenant-{tenantId}";
```

### Strategy 2: Shared bucket with prefix isolation

All tenants share a bucket; paths are prefixed with the tenant ID.

- Simpler bucket management
- Weaker isolation — single bucket policy applies to all tenants
- Suitable when strong isolation is not a compliance requirement

```csharp
var path = StoragePath.From("tenants", tenantId, "documents", fileName);
// No BucketOverride — uses default bucket
```

### Strategy 3: Bucket per tenant tier (balanced)

Group tenants by tier (free/pro/enterprise) into tier-specific buckets, with path prefixes for individual tenants within the tier.

```csharp
var bucket = tenant.Tier switch
{
    TenantTier.Enterprise => $"enterprise-{tenant.Region}",
    TenantTier.Pro        => $"pro-{tenant.Region}",
    _                     => "shared-storage"
};
var path = StoragePath.From(tenantId, "documents", fileName);
```

---

## Complete multi-tenant API example (ASP.NET Core)

```csharp
[ApiController]
[Route("tenants/{tenantId}/files")]
public class TenantFilesController : ControllerBase
{
    private readonly IStorageProvider _storage;
    private readonly ITenantContextResolver _tenantContext;

    public TenantFilesController(
        IStorageProvider storage,
        ITenantContextResolver tenantContext)
    {
        _storage = storage;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    public async Task<IActionResult> Upload(
        string tenantId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        // Authorize: ensure the caller has access to this tenant's data
        if (!await _tenantContext.CanAccessAsync(tenantId, User))
            return Forbid();

        var bucket = await ResolveBucketAsync(tenantId);
        var path = StoragePath.From("uploads", file.FileName);

        using var stream = file.OpenReadStream();

        var result = await _storage.UploadAsync(new UploadRequest
        {
            Path = path,
            Content = stream,
            ContentType = file.ContentType,
            ContentLength = file.Length,
            BucketOverride = bucket,
            Metadata = new Dictionary<string, string>
            {
                ["tenant-id"] = tenantId,
                ["uploaded-by"] = User.Identity?.Name ?? "unknown"
            }
        }, cancellationToken: cancellationToken);

        if (!result.IsSuccess)
            return StatusCode(502, new { error = result.ErrorMessage });

        return Ok(new
        {
            path = result.Value!.Path,
            sizeBytes = result.Value.SizeBytes,
            uploadedAt = result.Value.UploadedAt
        });
    }

    [HttpGet("{**filePath}")]
    public async Task<IActionResult> Download(
        string tenantId,
        string filePath,
        CancellationToken cancellationToken)
    {
        if (!await _tenantContext.CanAccessAsync(tenantId, User))
            return Forbid();

        var bucket = await ResolveBucketAsync(tenantId);

        var result = await _storage.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From(filePath),
            BucketOverride = bucket
        }, cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.ErrorCode == StorageErrorCode.FileNotFound)
                return NotFound();
            return StatusCode(502, new { error = result.ErrorMessage });
        }

        var metadata = await _storage.GetMetadataAsync(filePath, cancellationToken);
        var contentType = metadata.Value?.ContentType ?? "application/octet-stream";

        return File(result.Value!, contentType, Path.GetFileName(filePath));
    }

    [HttpDelete("{**filePath}")]
    public async Task<IActionResult> Delete(
        string tenantId,
        string filePath,
        CancellationToken cancellationToken)
    {
        if (!await _tenantContext.CanAccessAsync(tenantId, User))
            return Forbid();

        var result = await _storage.DeleteAsync(filePath, cancellationToken);

        return result.IsSuccess ? NoContent() : StatusCode(502, new { error = result.ErrorMessage });
    }

    [HttpGet]
    public async Task<IActionResult> ListFiles(
        string tenantId,
        [FromQuery] string? prefix,
        CancellationToken cancellationToken)
    {
        if (!await _tenantContext.CanAccessAsync(tenantId, User))
            return Forbid();

        var files = new List<object>();

        await foreach (var entry in _storage.ListAllAsync(prefix, cancellationToken))
        {
            files.Add(new
            {
                path = entry.Path,
                sizeBytes = entry.SizeBytes,
                contentType = entry.ContentType,
                lastModified = entry.LastModified
            });
        }

        return Ok(files);
    }

    private Task<string> ResolveBucketAsync(string tenantId)
    {
        // Simple strategy: bucket name derived from tenant ID
        return Task.FromResult($"tenant-{tenantId}");
    }
}
```

---

## Bucket provisioning on tenant creation

When using one bucket per tenant, create the bucket as part of your tenant onboarding flow:

```csharp
public async Task ProvisionTenantStorageAsync(string tenantId)
{
    // This is provider-specific — use the cloud SDK directly for bucket creation
    // ValiBlob does not provide a "CreateBucket" abstraction (bucket creation is an
    // administrative operation, not a per-file operation)

    // AWS example:
    // await _s3Client.PutBucketAsync(new PutBucketRequest
    // {
    //     BucketName = $"tenant-{tenantId}",
    //     UseClientRegion = true
    // });

    // Then record the bucket name in your tenant record
    await _tenantRepository.UpdateStorageBucketAsync(tenantId, $"tenant-{tenantId}");
}
```
