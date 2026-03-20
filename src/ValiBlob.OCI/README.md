# ValiBlob.OCI

Oracle Cloud Infrastructure (OCI) Object Storage provider for ValiBlob.

Supports standard object operations, resumable uploads (OCI multipart upload), and presigned URLs via OCI **Pre-Authenticated Requests (PARs)**. Unlike AWS and GCP, presigned URLs in OCI require an API call to create each PAR — there is no local signing. Metadata can only be set at upload time — in-place metadata updates are not supported by OCI Object Storage.

## Install

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.OCI
```

## Configuration

```json
{
  "ValiBlob": {
    "DefaultProvider": "OCI"
  },
  "ValiBlob:OCI": {
    "Namespace":      "my-namespace",
    "Bucket":         "my-bucket",
    "Region":         "us-ashburn-1",
    "TenancyId":      "ocid1.tenancy.oc1...",
    "UserId":         "ocid1.user.oc1...",
    "Fingerprint":    "xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx:xx",
    "PrivateKeyPath": "/run/secrets/oci_api_key.pem"
  }
}
```

Store `PrivateKeyPath` or the key content via environment variables or a secrets manager.

## Register

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.OCI.Extensions;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "OCI")
    .UseOCI();
```

## Features

| Feature | Supported |
|---|---|
| Upload / Download / Delete / List | Yes |
| Metadata (set at upload time) | Yes |
| In-place metadata update | No — re-upload required |
| Presigned URLs | Yes — via OCI Pre-Authenticated Requests (PARs) |
| Resumable uploads (chunked) | Yes |
| BucketOverride per request | Yes |

## Documentation

[OCI Object Storage provider docs](https://vali-blob-docs.netlify.app/docs/providers/oci)
