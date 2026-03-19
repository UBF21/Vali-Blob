# Supabase Storage Provider

The `ValiBlob.Supabase` package communicates with the Supabase Storage REST API via `HttpClient` and provides full `IStorageProvider` support for Supabase Storage.

---

## Installation

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.Supabase
```

---

## Authentication

Supabase Storage authenticates using the project URL and an API key. Use either:

- **Service role key** — full access, bypasses Row Level Security (RLS). Suitable for server-side backends.
- **Anon key** — subject to RLS policies. Suitable for client-facing servers with narrow permissions.

```json
{
  "ValiBlob:Supabase": {
    "Url": "https://xyzcompany.supabase.co",
    "ApiKey": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "Bucket": "public"
  }
}
```

The provider sets the `Authorization: Bearer {ApiKey}` and `apikey: {ApiKey}` headers on all requests automatically.

> **⚠️ Warning:** The service role key has full access to your Supabase project. Never expose it in client-side code or commit it to source control. Store it in environment variables or a secrets manager.

---

## Full `SupabaseStorageOptions` reference

| Property | Type | Default | Description |
|---|---|---|---|
| `Url` | `string` | `""` | Supabase project URL, e.g. `https://xyzcompany.supabase.co` |
| `ApiKey` | `string` | `""` | Service role key or anon key |
| `Bucket` | `string` | `""` | Default bucket name |
| `CdnBaseUrl` | `string?` | `null` | CDN prefix substituted in `GetUrlAsync` responses |

Configuration section: `ValiBlob:Supabase`

---

## `appsettings.json` example

```json
{
  "ValiBlob": {
    "DefaultProvider": "Supabase"
  },
  "ValiBlob:Supabase": {
    "Url": "https://xyzcompany.supabase.co",
    "ApiKey": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "Bucket": "documents",
    "CdnBaseUrl": "https://xyzcompany.supabase.co/storage/v1/render/image/public"
  }
}
```

---

## Code-only configuration

```csharp
builder.Services
    .AddValiBlob(o => o.DefaultProvider = "Supabase")
    .UseSupabase(opts =>
    {
        opts.Url = builder.Configuration["Supabase:Url"]!;
        opts.ApiKey = builder.Configuration["Supabase:ServiceRoleKey"]!;
        opts.Bucket = "documents";
    });
```

---

## Public vs private buckets

Supabase buckets can be public (files accessible via URL without authentication) or private (require a signed URL).

- **Public bucket**: `GetUrlAsync` returns a direct URL that anyone can access.
- **Private bucket**: Use `IPresignedUrlProvider` to generate time-limited signed download URLs.

Configure bucket visibility in the Supabase dashboard under **Storage → Buckets**. ValiBlob respects the bucket's visibility setting automatically.

---

## Presigned URLs

The Supabase provider implements `IPresignedUrlProvider`. Presigned URLs in Supabase are called "signed URLs" and are generated via the Storage REST API.

```csharp
public class FileShareService
{
    private readonly IStorageProvider _storage;

    public FileShareService(IStorageProvider storage) => _storage = storage;

    public async Task<string> CreateShareLinkAsync(string filePath, TimeSpan validFor)
    {
        if (_storage is not IPresignedUrlProvider presigned)
            throw new NotSupportedException("Provider does not support presigned URLs.");

        var result = await presigned.GetPresignedDownloadUrlAsync(filePath, validFor);

        if (!result.IsSuccess)
            throw new Exception($"Failed to create share link: {result.ErrorMessage}");

        return result.Value!;
    }
}
```

---

## `SetMetadataAsync` limitation

The Supabase Storage REST API does not expose a metadata update endpoint. Calling `SetMetadataAsync` on the Supabase provider returns a failure result with `StorageErrorCode.NotSupported`.

If you need to associate metadata with files in Supabase, consider:
- Storing metadata in a Supabase database table keyed by file path
- Encoding metadata in the file path or file name

---

## BucketOverride

Use `BucketOverride` on `UploadRequest` or `DownloadRequest` to target a different Supabase bucket for that specific operation.

```csharp
// Upload to a tenant-specific bucket
var result = await _storage.UploadAsync(new UploadRequest
{
    Path = StoragePath.From("invoices", "inv-2024-001.pdf"),
    Content = pdfStream,
    ContentType = "application/pdf",
    BucketOverride = "tenant-acme-files"
});

// Download from the same bucket
var download = await _storage.DownloadAsync(new DownloadRequest
{
    Path = StoragePath.From("invoices", "inv-2024-001.pdf"),
    BucketOverride = "tenant-acme-files"
});
```

---

## HttpClient registration

The Supabase provider is registered with `AddHttpClient`, which means it participates in the ASP.NET Core `IHttpClientFactory` lifecycle. The `HttpClient` base address and authentication headers are configured automatically:

```
BaseAddress: {Url}/storage/v1/
Authorization: Bearer {ApiKey}
apikey: {ApiKey}
```

You can customize the `HttpClient` further (e.g., adding retry policies via Polly) by accessing the named client after registration:

```csharp
builder.Services
    .AddValiBlob()
    .UseSupabase();

// Add Polly retry policy to the Supabase HttpClient
builder.Services
    .AddHttpClient<SupabaseStorageProvider>()
    .AddTransientHttpErrorPolicy(p =>
        p.WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(Math.Pow(2, i))));
```

---

## Limitations

- `SetMetadataAsync` is not supported — returns `StorageErrorCode.NotSupported`.
- `UploadFromUrlAsync` is not supported natively by the Supabase Storage REST API; the provider performs the fetch server-side and re-uploads.
- Supabase free tier has storage and bandwidth limits. Check the Supabase pricing page for current limits.
- File paths in Supabase Storage must not begin with `/`.
- Bucket names must be unique within a Supabase project.
- The maximum upload size via the REST API depends on your Supabase plan and any configured limits in the project settings.
