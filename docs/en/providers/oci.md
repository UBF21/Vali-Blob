# Oracle Cloud Infrastructure (OCI) Object Storage Provider

The `ValiBlob.OCI` package wraps the `OCI.DotNetSDK.Objectstorage` SDK and provides full `IStorageProvider` support for Oracle Cloud Infrastructure Object Storage.

---

## Installation

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.OCI
```

---

## Authentication options

### API Key (explicit configuration)

OCI uses API key-based authentication. Provide the tenancy OCID, user OCID, key fingerprint, and path to the PEM private key file. This maps to `SimpleAuthenticationDetailsProvider`.

```json
{
  "ValiBlob:OCI": {
    "Namespace": "my-tenancy-namespace",
    "Bucket": "my-oci-bucket",
    "Region": "sa-saopaulo-1",
    "TenancyId": "ocid1.tenancy.oc1..aaaaaaaaxxx",
    "UserId": "ocid1.user.oc1..aaaaaaaaxxx",
    "Fingerprint": "aa:bb:cc:dd:ee:ff:00:11:22:33:44:55:66:77:88:99",
    "PrivateKeyPath": "/etc/oci/oci_api_key.pem"
  }
}
```

All four fields (`TenancyId`, `UserId`, `Fingerprint`, `PrivateKeyPath`) must be non-null for explicit API key authentication.

### OCI config file (default fallback)

When any of the four required API key fields is `null`, the provider falls back to `ConfigFileAuthenticationDetailsProvider("DEFAULT")`, which reads `~/.oci/config` using the `[DEFAULT]` profile.

```ini
# ~/.oci/config
[DEFAULT]
user=ocid1.user.oc1..aaaaaaaaxxx
fingerprint=aa:bb:cc:dd:ee:ff:00:11:22:33:44:55:66:77:88:99
tenancy=ocid1.tenancy.oc1..aaaaaaaaxxx
region=sa-saopaulo-1
key_file=~/.oci/oci_api_key.pem
```

This is the recommended approach for local development.

### Instance Principal (OCI Compute)

When running on OCI Compute instances, configure Instance Principal authentication by overriding the `ObjectStorageClient` registration:

```csharp
using Oci.Common.Auth;
using Oci.ObjectstorageService;

builder.Services
    .AddValiBlob()
    .UseOCI();

// Override with instance principal
builder.Services.AddSingleton(_ =>
    new ObjectStorageClient(new InstancePrincipalsAuthenticationDetailsProvider()));
```

---

## Full `OCIStorageOptions` reference

| Property | Type | Default | Description |
|---|---|---|---|
| `Namespace` | `string` | `""` | OCI Object Storage namespace (tenancy namespace) |
| `Bucket` | `string` | `""` | Default bucket name |
| `Region` | `string` | `"sa-saopaulo-1"` | OCI region identifier |
| `TenancyId` | `string?` | `null` | Tenancy OCID |
| `UserId` | `string?` | `null` | User OCID |
| `Fingerprint` | `string?` | `null` | API key fingerprint (colon-separated hex) |
| `PrivateKeyPath` | `string?` | `null` | Path to the PEM private key file |
| `PrivateKeyContent` | `string?` | `null` | PEM private key content as string (alternative to file path) |
| `CdnBaseUrl` | `string?` | `null` | CDN prefix substituted in `GetUrlAsync` responses |

Configuration section: `ValiBlob:OCI`

---

## `appsettings.json` example

```json
{
  "ValiBlob": {
    "DefaultProvider": "OCI"
  },
  "ValiBlob:OCI": {
    "Namespace": "axxxxxxxxxxx",
    "Bucket": "production-assets",
    "Region": "sa-saopaulo-1",
    "TenancyId": "ocid1.tenancy.oc1..aaaaaaaaxxx",
    "UserId": "ocid1.user.oc1..aaaaaaaaxxx",
    "Fingerprint": "aa:bb:cc:dd:ee:ff:00:11:22:33:44:55:66:77:88:99",
    "PrivateKeyPath": "/etc/oci/oci_api_key.pem",
    "CdnBaseUrl": "https://assets.example.com"
  }
}
```

---

## Code-only configuration

```csharp
builder.Services
    .AddValiBlob(o => o.DefaultProvider = "OCI")
    .UseOCI(opts =>
    {
        opts.Namespace = "axxxxxxxxxxx";
        opts.Bucket = "production-assets";
        opts.Region = "sa-saopaulo-1";
        opts.TenancyId = Environment.GetEnvironmentVariable("OCI_TENANCY_ID");
        opts.UserId = Environment.GetEnvironmentVariable("OCI_USER_ID");
        opts.Fingerprint = Environment.GetEnvironmentVariable("OCI_FINGERPRINT");
        opts.PrivateKeyPath = Environment.GetEnvironmentVariable("OCI_KEY_FILE");
    });
```

---

## `SetMetadataAsync` limitation

OCI Object Storage does not support in-place metadata updates. `SetMetadataAsync` on the OCI provider returns a `StorageResult` with `StorageErrorCode.NotSupported` — it does not attempt a re-upload or server-side copy.

```csharp
var result = await _storage.SetMetadataAsync("uploads/file.pdf", metadata);

if (!result.IsSuccess && result.ErrorCode == StorageErrorCode.NotSupported)
{
    // OCI does not support in-place metadata updates.
    // To change metadata you must re-upload the object with the new metadata values.
}
```

To update metadata on an existing OCI object, re-upload it with the desired metadata set in `UploadRequest.Metadata`. For workloads that update metadata frequently, consider encoding it in the object path or storing it in a separate index.

---

## BucketOverride

Use `BucketOverride` to direct a specific operation to a different OCI bucket:

```csharp
var result = await _storage.UploadAsync(new UploadRequest
{
    Path = StoragePath.From("backups", "db-2024-03-15.sql.gz"),
    Content = backupStream,
    ContentType = "application/gzip",
    BucketOverride = "cold-archive-bucket"
});
```

---

## Finding your namespace

Your tenancy namespace is displayed in the OCI console under **Object Storage → Buckets** and also available via the OCI CLI:

```bash
oci os ns get
```

---

## Presigned URLs (Pre-Authenticated Requests)

`OCIStorageProvider` implements `IPresignedUrlProvider` using OCI **Pre-Authenticated Requests (PARs)**.

### How OCI PARs differ from AWS/GCP presigned URLs

| | AWS S3 / GCP Cloud Storage | OCI Object Storage |
|---|---|---|
| **How the URL is generated** | Signed locally in memory using your credentials — **no network call** | A PAR object is **created on OCI's servers** via an API call — requires a round-trip |
| **Network cost per URL** | Zero — pure CPU operation | One HTTP request to OCI per URL generated |
| **Latency** | Sub-millisecond | Depends on network latency to OCI (~50–200 ms) |
| **Server-side lifecycle** | Stateless — no cloud record of the URL | PAR is a real object: can be listed, deactivated, or deleted from the Console or CLI |
| **Rate limits** | None for URL generation | OCI imposes API rate limits on PAR creation |

In low-to-moderate-traffic scenarios the extra round-trip is negligible. For high-frequency URL generation, see the caching note below.

### Usage

```csharp
var provider = factory.Create("oci");

if (provider is IPresignedUrlProvider presigned)
{
    // Creates a PAR on OCI granting PUT access for 15 minutes
    var uploadUrl = await presigned.GetPresignedUploadUrlAsync(
        StoragePath.From("uploads", userId, "report.pdf"),
        expiresIn: TimeSpan.FromMinutes(15));

    // Creates a PAR on OCI granting GET access for 2 hours
    var downloadUrl = await presigned.GetPresignedDownloadUrlAsync(
        "private/report.pdf",
        expiresIn: TimeSpan.FromHours(2));
}
```

### Caching

Because each URL generation makes an HTTP call to OCI, cache the URL when the same user accesses the same resource repeatedly within the validity window. Always scope the cache key per user — a PAR grants unauthenticated access for its entire lifetime, so sharing it across users is a security risk.

```csharp
var cacheKey = $"oci-par:{userId}:{path}";
if (!cache.TryGetValue(cacheKey, out string? url))
{
    var expiration = TimeSpan.FromHours(2);
    var result = await presigned.GetPresignedDownloadUrlAsync(path, expiration);
    url = result.Value;
    cache.Set(cacheKey, url, expiration * 0.9);
}
```

### PAR lifecycle management

PARs are real server-side objects. You can view, deactivate, or delete them under **Storage → Buckets → \<bucket\> → Pre-Authenticated Requests** in the OCI Console, or via CLI:

```bash
oci os preauth-request list --bucket-name my-bucket --namespace my-namespace
```

This lets you revoke access to a URL after it has been issued — not possible with AWS or GCP presigned URLs.

---

## Limitations

- `SetMetadataAsync` returns `NotSupported` — OCI does not allow in-place metadata updates. Re-upload the object with the new metadata instead.
- OCI Object Storage object names are limited to 1024 bytes.
- PAR maximum expiry varies by OCI region policy; keep expiry windows short for sensitive objects.
- The OCI SDK for .NET does not support password-protected private keys when using `FilePrivateKeySupplier`. Ensure the PEM file is not password-protected, or handle decryption before providing the key content.
- OCI regions have specific tenancy namespace values. Always confirm the namespace matches the region where the bucket resides.
