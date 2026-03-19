# CDN Integration

ValiBlob provides an `ICdnProvider` abstraction that maps storage paths to CDN URLs. This decouples your application from a specific CDN vendor and makes it easy to add cache invalidation hooks.

---

## The `ICdnProvider` interface

```csharp
public interface ICdnProvider
{
    string GetCdnUrl(string storagePath);
    Task InvalidateCacheAsync(string storagePath, CancellationToken cancellationToken = default);
}
```

| Method | Description |
|---|---|
| `GetCdnUrl` | Returns the public CDN URL for a storage path |
| `InvalidateCacheAsync` | Sends a cache invalidation request to the CDN for the given path |

---

## `PrefixCdnProvider` — the built-in provider

`PrefixCdnProvider` maps a storage path to a CDN URL by combining a configurable base URL with the path:

```
storagePath = "avatars/user-42/profile.jpg"
BaseUrl     = "https://cdn.example.com"

result      = "https://cdn.example.com/avatars/user-42/profile.jpg"
```

### Registration

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithCdn(o =>
    {
        o.BaseUrl = "https://cdn.example.com";
    });
```

### `CdnOptions`

| Property | Type | Default | Description |
|---|---|---|---|
| `BaseUrl` | `string` | `""` | Base CDN URL, e.g. `https://cdn.example.com` |
| `StripPrefix` | `string?` | `null` | Optional path prefix to strip from storage paths before appending to the base URL |

### Using `StripPrefix`

Some storage layouts use a bucket or environment prefix that should not appear in CDN URLs:

```csharp
.WithCdn(o =>
{
    o.BaseUrl = "https://cdn.example.com";
    o.StripPrefix = "production/"; // "production/images/cat.jpg" → CDN: "images/cat.jpg"
});
```

---

## Using `ICdnProvider` in your code

Inject `ICdnProvider` wherever you need to build public URLs:

```csharp
public class FileUrlService
{
    private readonly ICdnProvider _cdn;

    public FileUrlService(ICdnProvider cdn) => _cdn = cdn;

    public string GetPublicUrl(string storagePath)
    {
        return _cdn.GetCdnUrl(storagePath);
        // "https://cdn.example.com/avatars/user-42/profile.jpg"
    }

    public async Task PurgeAsync(string storagePath, CancellationToken ct)
    {
        await _cdn.InvalidateCacheAsync(storagePath, ct);
    }
}
```

---

## Implementing a custom provider

`PrefixCdnProvider` does not make real API calls to invalidate cache — its `InvalidateCacheAsync` is a no-op. For real CDN integration, implement `ICdnProvider` and call the vendor API:

### CloudFront skeleton

```csharp
using Amazon.CloudFront;
using ValiBlob.Core.Abstractions;

public sealed class CloudFrontCdnProvider : ICdnProvider
{
    private readonly IAmazonCloudFront _cloudFront;
    private readonly string _distributionId;
    private readonly string _baseUrl;

    public CloudFrontCdnProvider(
        IAmazonCloudFront cloudFront,
        IOptions<CloudFrontOptions> options)
    {
        _cloudFront = cloudFront;
        _distributionId = options.Value.DistributionId;
        _baseUrl = options.Value.BaseUrl.TrimEnd('/');
    }

    public string GetCdnUrl(string storagePath)
        => $"{_baseUrl}/{storagePath.TrimStart('/')}";

    public async Task InvalidateCacheAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        await _cloudFront.CreateInvalidationAsync(new CreateInvalidationRequest
        {
            DistributionId = _distributionId,
            InvalidationBatch = new InvalidationBatch
            {
                Paths = new Paths { Items = new List<string> { $"/{storagePath}" }, Quantity = 1 },
                CallerReference = Guid.NewGuid().ToString()
            }
        }, cancellationToken);
    }
}
```

### Cloudflare skeleton

```csharp
public sealed class CloudflareCdnProvider : ICdnProvider
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _zoneId;

    public string GetCdnUrl(string storagePath)
        => $"{_baseUrl.TrimEnd('/')}/{storagePath.TrimStart('/')}";

    public async Task InvalidateCacheAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.cloudflare.com/client/v4/zones/{_zoneId}/purge_cache";
        var body = JsonSerializer.Serialize(new { files = new[] { GetCdnUrl(storagePath) } });
        await _http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), cancellationToken);
    }
}
```

### Registering a custom provider

```csharp
// Replace the default PrefixCdnProvider
builder.Services.AddSingleton<ICdnProvider, CloudFrontCdnProvider>();
```

---

## Cache invalidation hook

Call `InvalidateCacheAsync` after deleting or replacing a file to ensure stale content is purged from the CDN edge:

```csharp
public async Task ReplaceFileAsync(string path, Stream newContent, CancellationToken ct)
{
    // Upload the new version
    await _storage.UploadAsync(new UploadRequest
    {
        Path = StoragePath.From(path),
        Content = newContent,
        ConflictResolution = ConflictResolution.Overwrite
    }, cancellationToken: ct);

    // Purge CDN cache so the new version is served immediately
    await _cdn.InvalidateCacheAsync(path, ct);
}
```
