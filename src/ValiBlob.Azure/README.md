# ValiBlob.Azure

Azure Blob Storage provider for ValiBlob.

Supports standard blob operations, SAS token presigned URLs, resumable uploads, and optional automatic container creation.

## Install

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.Azure
```

## Configuration

**With connection string:**

```json
{
  "ValiBlob": {
    "DefaultProvider": "Azure"
  },
  "ValiBlob:Azure": {
    "Container":                "my-files",
    "ConnectionString":         "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net",
    "CreateContainerIfNotExists": true
  }
}
```

**With account name and key:**

```json
{
  "ValiBlob:Azure": {
    "Container":   "my-files",
    "AccountName": "mystorageaccount",
    "AccountKey":  ""
  }
}
```

Set `AccountKey` or the `ConnectionString` via environment variables — never commit credentials to source control.

## Register

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Azure.Extensions;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "Azure")
    .UseAzure();
```

## Features

| Feature | Supported |
|---|---|
| Upload / Download / Delete / List | Yes |
| Presigned upload URL (SAS) | Yes |
| Presigned download URL (SAS) | Yes |
| Resumable uploads (chunked) | Yes |
| BucketOverride per request | Yes |
| Automatic container creation | Yes (opt-in) |

## Documentation

[Azure Blob Storage provider docs](../../docs/en/providers/azure.md)
