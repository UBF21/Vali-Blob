# ValiBlob.Local

Local filesystem storage provider for ValiBlob — ideal for development, testing, and offline scenarios.

## Features

- Full `IStorageProvider` implementation backed by the local filesystem
- Resumable (multi-chunk) uploads via `IResumableUploadProvider` — chunks stored as temp files and assembled on complete
- Presigned URL stubs via `IPresignedUrlProvider`
- Sidecar `.meta.json` files for content-type and custom metadata persistence
- Range download support
- Path traversal protection

## Installation

```
dotnet add package ValiBlob.Local
```

## Usage

```csharp
services.AddValiBlob()
    .UseLocal(opts =>
    {
        opts.BasePath = "/var/app/storage";
        opts.PublicBaseUrl = "http://localhost:5000/files";
        opts.CreateIfNotExists = true;
    });
```

### Bind from `appsettings.json`

```json
{
  "ValiBlob": {
    "Local": {
      "BasePath": "/var/app/storage",
      "PublicBaseUrl": "http://localhost:5000/files",
      "CreateIfNotExists": true
    }
  }
}
```

```csharp
services.AddValiBlob()
    .UseLocal(configuration);
```

## Options

| Property | Default | Description |
|---|---|---|
| `BasePath` | `""` | Root directory for file storage. Required. |
| `CreateIfNotExists` | `true` | Create `BasePath` on startup if missing. |
| `PublicBaseUrl` | `null` | Base URL for `GetUrlAsync`. Falls back to `file://` URI if not set. |
| `PreserveDirectoryStructure` | `true` | Preserve path hierarchy inside `BasePath`. |
