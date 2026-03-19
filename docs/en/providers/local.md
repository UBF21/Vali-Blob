# Local Filesystem Provider

The `ValiBlob.Local` package implements `IStorageProvider` against the local filesystem. It is designed for development, testing, and Docker Compose environments where cloud storage is unnecessary or unavailable.

---

## Overview

`ValiBlob.Local` stores files as ordinary files on disk, inside a configured root directory (`BasePath`). It supports the full `IStorageProvider` surface — upload, download, delete, copy, move, list, metadata, folder operations — as well as resumable uploads and presigned URL stubs.

### When to use it

| Scenario | Recommended |
|---|---|
| Local development without cloud credentials | Yes |
| Docker Compose environments | Yes |
| CI/CD pipelines without cloud access | Yes |
| Integration tests for storage-related code | Yes |
| Production workloads | No |

For production use AWS, Azure, GCP, OCI, or Supabase. The local provider offers no redundancy, no CDN, and no horizontal scalability.

---

## Installation

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.Local
```

---

## Configuration

### `appsettings.json`

```json
{
  "ValiBlob": {
    "Local": {
      "BasePath": "/var/storage",
      "CreateIfNotExists": true,
      "PublicBaseUrl": "http://localhost:5000/files",
      "PreserveDirectoryStructure": true
    }
  }
}
```

### DI registration in `Program.cs`

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Local.Extensions;

builder.Services
    .AddValiBlob()
    .UseLocal(o =>
    {
        o.BasePath = "/var/storage";
        o.PublicBaseUrl = "http://localhost:5000/files";
    });
```

### `LocalStorageOptions` reference

| Property | Type | Default | Description |
|---|---|---|---|
| `BasePath` | `string` | `""` (required) | Root directory where all files are stored |
| `CreateIfNotExists` | `bool` | `true` | Creates `BasePath` automatically if it does not exist |
| `PublicBaseUrl` | `string?` | `null` | Base URL prepended to paths in `GetUrlAsync`. If unset, returns a `file://` URI |
| `PreserveDirectoryStructure` | `bool` | `true` | Keeps the original path hierarchy inside `BasePath` |

---

## Features

| Feature | Support |
|---|---|
| Upload / Download / Delete | Full |
| Copy / Move | Full |
| Exists / GetUrl | Full |
| List files (prefix, pagination) | Full |
| Batch delete | Full |
| Folder operations (`DeleteFolderAsync`, `ListFoldersAsync`) | Full |
| Metadata via sidecar files | Full |
| Resumable uploads | Full |
| Presigned URL stubs | Yes (token-based, local dev only) |
| Range downloads | Yes (partial file reads) |

---

## Basic usage

The local provider is consumed through the same `IStorageProvider` interface as any cloud provider:

```csharp
public class DocumentService
{
    private readonly IStorageProvider _storage;

    public DocumentService(IStorageProvider storage) => _storage = storage;

    public async Task<string> SaveAsync(Stream content, string fileName)
    {
        var result = await _storage.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("documents", fileName),
            Content = content,
            ContentType = "application/pdf"
        });

        if (!result.IsSuccess)
            throw new Exception($"Upload failed: {result.ErrorMessage}");

        return result.Value!.Path;
    }

    public async Task<Stream> LoadAsync(string fileName)
    {
        var result = await _storage.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From("documents", fileName)
        });

        if (!result.IsSuccess)
            throw new Exception($"Download failed: {result.ErrorMessage}");

        return result.Value!;
    }
}
```

---

## Resumable uploads

`ValiBlob.Local` fully implements `IResumableUploadProvider`. Chunks are stored as temporary files and assembled when the upload is completed.

```csharp
// Inject IResumableUploadProvider from DI
var session = await _resumable.InitiateResumableUploadAsync(new ResumableUploadRequest
{
    Path = StoragePath.From("videos", "intro.mp4"),
    ContentType = "video/mp4",
    TotalSize = fileStream.Length
});

// Upload each chunk
var chunkSize = 5 * 1024 * 1024; // 5 MB
var buffer = new byte[chunkSize];
int bytesRead;
long offset = 0;

while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
{
    await _resumable.UploadChunkAsync(new ResumableChunkRequest
    {
        SessionId = session.Value!.SessionId,
        ChunkIndex = (int)(offset / chunkSize),
        Data = new MemoryStream(buffer, 0, bytesRead),
        Offset = offset
    });
    offset += bytesRead;
}

// Assemble the final file
var result = await _resumable.CompleteResumableUploadAsync(session.Value!.SessionId);
```

### Resumable upload internals

Chunks are stored under `{BasePath}/.resumable/{uploadId}/{offset}.chunk`. Session state is persisted as a JSON file alongside the chunks. On `CompleteResumableUploadAsync`, all chunks are assembled in order into the final file at the target path, and the temporary chunk directory is deleted.

---

## Sidecar metadata files

File metadata (content type, custom key-value pairs, upload timestamps) is stored in a `.meta.json` file adjacent to the data file.

For a file at `BasePath/documents/report.pdf`, the metadata lives at `BasePath/documents/report.pdf.meta.json`.

The sidecar format is a flat JSON object:

```json
{
  "content-type": "application/pdf",
  "x-uploaded-by": "user-123",
  "x-department": "finance"
}
```

Sidecar files are managed transparently by the provider. You interact with metadata through the standard `GetMetadataAsync` and `SetMetadataAsync` methods.

---

## Presigned URLs (local dev stubs)

`ValiBlob.Local` produces token-based presigned URLs that are suitable for local development workflows but are not backed by any real access control. They should not be used as a security mechanism.

```csharp
// Produces a URL like: http://localhost:5000/files/documents/report.pdf?token=<guid>&expires=<unix-ts>
var url = await _presigned.GetPresignedDownloadUrlAsync(new PresignedUrlRequest
{
    Path = StoragePath.From("documents", "report.pdf"),
    Expiration = TimeSpan.FromMinutes(15)
});
```

---

## Switching between local and cloud

A common pattern is to use `ValiBlob.Local` in development and a cloud provider in production, with no changes to service code:

```csharp
if (builder.Environment.IsDevelopment())
{
    builder.Services
        .AddValiBlob()
        .UseLocal(o => o.BasePath = "./local-storage");
}
else
{
    builder.Services
        .AddValiBlob()
        .UseAWS(builder.Configuration.GetSection("ValiBlob:AWS"));
}
```

Because all business code depends only on `IStorageProvider`, the switch is entirely contained in the composition root.

---

## Docker Compose example

```yaml
services:
  api:
    build: .
    volumes:
      - storage-data:/var/storage
    environment:
      ValiBlob__Local__BasePath: /var/storage
      ValiBlob__Local__PublicBaseUrl: http://localhost:5000/files

volumes:
  storage-data:
```

---

## Limitations

- **No redundancy:** Files are stored on a single disk. There is no built-in replication or backup.
- **No scalability:** Does not work across multiple application instances sharing a filesystem unless a shared network volume is mounted.
- **Presigned URL stubs are not secure:** The token-based URLs generated by this provider do not enforce access control on their own. Do not expose them to untrusted clients in production.
- **Not recommended for production:** Use a cloud provider for any workload that requires durability, availability, or CDN distribution.
