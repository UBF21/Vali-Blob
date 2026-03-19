# ValiBlob.Supabase

Supabase Storage provider for ValiBlob.

Uses the Supabase Storage REST API for standard operations and the TUS protocol for resumable uploads. Supports both public and private buckets.

## Install

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.Supabase
```

## Configuration

```json
{
  "ValiBlob": {
    "DefaultProvider": "Supabase"
  },
  "ValiBlob:Supabase": {
    "Url":    "https://your-project.supabase.co",
    "ApiKey": "",
    "Bucket": "my-bucket"
  }
}
```

Set `ApiKey` via environment variables or a secrets manager. Use the `service_role` key for server-side operations with full access, or a scoped key with RLS policies for user-facing operations.

## Register

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Supabase.Extensions;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "Supabase")
    .UseSupabase();
```

## Features

| Feature | Supported |
|---|---|
| Upload / Download / Delete / List | Yes |
| Public bucket URLs | Yes |
| Presigned download URL | Yes |
| Resumable uploads (TUS protocol) | Yes |
| BucketOverride per request | Yes |

## Resumable uploads (TUS)

Supabase Storage implements the [TUS resumable upload protocol](https://tus.io). ValiBlob's Supabase provider uses TUS for `IResumableUploadProvider`, enabling reliable large file uploads with pause and resume:

```csharp
var session = await _resumable.StartUploadAsync(new ResumableUploadRequest
{
    FileName    = "video.mp4",
    ContentType = "video/mp4",
    TotalSize   = fileInfo.Length
});

// Upload chunks — can be interrupted and resumed
await _resumable.UploadChunkAsync(new ResumableChunkRequest
{
    SessionId  = session.SessionId,
    ChunkIndex = 0,
    Data       = chunkStream
});

await _resumable.CompleteUploadAsync(session.SessionId);
```

## Documentation

[Supabase Storage provider docs](../../docs/en/providers/supabase.md)
