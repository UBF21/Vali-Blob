# ValiBlob.GCP

Google Cloud Storage provider for ValiBlob.

Supports standard object operations, resumable uploads, and presigned URLs when a service account credentials file is provided.

## Install

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.GCP
```

## Configuration

```json
{
  "ValiBlob": {
    "DefaultProvider": "GCP"
  },
  "ValiBlob:GCP": {
    "Bucket":          "my-app-bucket",
    "CredentialsPath": "/run/secrets/gcp-service-account.json"
  }
}
```

Alternatively, pass the JSON content directly:

```json
{
  "ValiBlob:GCP": {
    "Bucket":          "my-app-bucket",
    "CredentialsJson": "{ \"type\": \"service_account\", ... }"
  }
}
```

If neither `CredentialsPath` nor `CredentialsJson` is set, the provider falls back to Application Default Credentials (ADC). ADC works for most operations, but **presigned URLs are not supported with ADC** — a service account key is required.

## Register

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.GCP.Extensions;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "GCP")
    .UseGCP();
```

## Features

| Feature | Supported |
|---|---|
| Upload / Download / Delete / List | Yes |
| Presigned upload URL | Yes (service account required) |
| Presigned download URL | Yes (service account required) |
| Resumable uploads (chunked) | Yes |
| BucketOverride per request | Yes |

## Note on presigned URLs

Generating presigned URLs requires signing with a service account private key. If your application runs with ADC (Compute Engine metadata, Workload Identity), calling `GetPresignedUploadUrlAsync` or `GetPresignedDownloadUrlAsync` will return a `NotSupported` error. Provide `CredentialsPath` or `CredentialsJson` to enable this feature. See [Troubleshooting](../../docs/en/troubleshooting.md) for details.

## Documentation

[GCP Cloud Storage provider docs](../../docs/en/providers/gcp.md)
