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

OCI Object Storage does not support in-place metadata updates. The OCI provider handles `SetMetadataAsync` by performing a server-side copy of the object with the new metadata — this is transparent to the caller but has cost and latency implications:

1. A `CopyObject` operation is performed (billed as a PUT request)
2. The original object is deleted

For workloads that update metadata frequently, consider encoding metadata in the object path or as part of a separate index record.

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

## Limitations

- `SetMetadataAsync` requires a server-side copy — see the dedicated section above.
- OCI Object Storage object names are limited to 1024 bytes.
- Presigned URLs (pre-authenticated requests) have a maximum expiry of 7 days from creation.
- The OCI SDK for .NET does not support passwordless private keys when using `FilePrivateKeySupplier`. Ensure the PEM file is not password-protected, or handle decryption before providing the key content.
- OCI regions have specific tenancy namespace values. Always confirm the namespace matches the region where the bucket resides.
