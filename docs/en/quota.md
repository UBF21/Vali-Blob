# Storage Quotas

ValiBlob provides a quota system that tracks used storage per scope and rejects uploads that would exceed the configured limit. Quota enforcement is built into the pipeline and works with any storage provider.

---

## The `IStorageQuotaService` interface

```csharp
public interface IStorageQuotaService
{
    Task<long> GetUsedBytesAsync(string scope, CancellationToken cancellationToken = default);
    Task RecordUploadAsync(string scope, long bytes, CancellationToken cancellationToken = default);
    Task RecordDeleteAsync(string scope, long bytes, CancellationToken cancellationToken = default);
    Task<long?> GetQuotaLimitAsync(string scope, CancellationToken cancellationToken = default);
}
```

A **scope** is any string that identifies a billing or isolation unit — a tenant ID, a user ID, a bucket name, or any custom key your application defines.

---

## `InMemoryStorageQuotaService` — the default

The built-in implementation stores counters in a thread-safe `ConcurrentDictionary`. It is suitable for:

- Single-instance deployments
- Development and testing
- Applications where approximate tracking (reset on restart) is acceptable

Usage counters are **lost when the process restarts**. For persistent tracking, implement `IStorageQuotaService` backed by Redis, a database, or your cloud provider's native usage API (see the production note below).

---

## Configuration

| Property | Type | Default | Description |
|---|---|---|---|
| `Enabled` | `bool` | `false` | Opt-in: must be explicitly set to `true` |
| `DefaultLimitBytes` | `long?` | `null` | Limit applied to all scopes without a specific override. `null` = unlimited |
| `Limits` | `Dictionary<string, long>` | `{}` | Per-scope limit overrides |
| `ScopeResolver` | `Func<UploadRequest, string>?` | `null` | Custom function to derive the scope from the request. Default: `BucketOverride ?? "default"` |

---

## Registration

### Basic global quota

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithQuota(o =>
    {
        o.Enabled = true;
        o.DefaultLimitBytes = 5L * 1024 * 1024 * 1024; // 5 GB for everyone
    });
```

### Per-tenant quotas

Use `ScopeResolver` to derive the scope from each request, then configure per-tenant limits:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithQuota(o =>
    {
        o.Enabled = true;

        // Use the BucketOverride field as the tenant scope
        o.ScopeResolver = request => request.BucketOverride ?? "default";

        // Per-tenant limits (bytes)
        o.Limits["tenant-free"] = 512L * 1024 * 1024;        // 512 MB
        o.Limits["tenant-pro"] = 50L * 1024 * 1024 * 1024;   // 50 GB
        o.Limits["tenant-enterprise"] = long.MaxValue;         // unlimited

        // Fallback for tenants not listed above
        o.DefaultLimitBytes = 1L * 1024 * 1024 * 1024; // 1 GB
    });
```

Upload with a per-tenant scope:

```csharp
var request = new UploadRequest
{
    Path = StoragePath.From("reports", "Q4-2026.pdf"),
    Content = fileStream,
    ContentType = "application/pdf",
    BucketOverride = "tenant-pro" // scope = "tenant-pro"
};

var result = await _storage.UploadAsync(request);
```

### Custom scope resolver

The scope does not have to come from `BucketOverride`. You can resolve it from any property on the request, or from ambient context:

```csharp
o.ScopeResolver = request =>
{
    // Derive scope from the path prefix
    var segments = request.Path.ToString().Split('/');
    return segments.Length > 1 ? segments[0] : "default";
};
```

---

## Quota exceeded behaviour

When an upload would exceed the quota, the pipeline is cancelled and an exception is thrown with a descriptive message:

```
StorageQuotaExceededException: Quota exceeded for scope 'tenant-free'.
  Used: 536870912 bytes, Limit: 536870912 bytes, Requested: 10485760 bytes.
```

Handle it alongside other validation errors:

```csharp
try
{
    var result = await _storage.UploadAsync(request);
}
catch (StorageQuotaExceededException ex)
{
    return Results.StatusCode(429, new { error = ex.Message });
}
```

---

## Querying current usage

Inject `IStorageQuotaService` directly to display usage to users:

```csharp
public class StorageDashboardService
{
    private readonly IStorageQuotaService _quota;

    public StorageDashboardService(IStorageQuotaService quota) => _quota = quota;

    public async Task<StorageUsageSummary> GetUsageAsync(string tenantId)
    {
        var used = await _quota.GetUsedBytesAsync(tenantId);
        var limit = await _quota.GetQuotaLimitAsync(tenantId);

        return new StorageUsageSummary
        {
            UsedBytes = used,
            LimitBytes = limit,
            PercentUsed = limit.HasValue ? (double)used / limit.Value * 100 : 0
        };
    }
}
```

---

## Production note: replace for multi-instance deployments

`InMemoryStorageQuotaService` counters exist only in the current process. In a multi-instance deployment (Kubernetes, App Service with multiple replicas, etc.) each instance maintains its own separate counter, so the effective limit is multiplied by the number of instances.

For production, implement `IStorageQuotaService` backed by a shared store:

```csharp
// Redis-backed implementation (example skeleton)
public sealed class RedisStorageQuotaService : IStorageQuotaService
{
    private readonly IDatabase _db;
    private readonly QuotaOptions _options;

    public RedisStorageQuotaService(IConnectionMultiplexer redis, QuotaOptions options)
    {
        _db = redis.GetDatabase();
        _options = options;
    }

    public async Task<long> GetUsedBytesAsync(string scope, CancellationToken ct = default)
        => (long)(await _db.StringGetAsync($"quota:used:{scope}"));

    public async Task RecordUploadAsync(string scope, long bytes, CancellationToken ct = default)
        => await _db.StringIncrementAsync($"quota:used:{scope}", bytes);

    public async Task RecordDeleteAsync(string scope, long bytes, CancellationToken ct = default)
        => await _db.StringDecrementAsync($"quota:used:{scope}", bytes);

    public Task<long?> GetQuotaLimitAsync(string scope, CancellationToken ct = default)
    {
        if (_options.Limits.TryGetValue(scope, out var limit))
            return Task.FromResult<long?>(limit);
        return Task.FromResult(_options.DefaultLimitBytes);
    }
}

// Register it
builder.Services.AddSingleton<IStorageQuotaService, RedisStorageQuotaService>();
```
