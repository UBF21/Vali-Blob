# ValiBlob.Sample

A minimal ASP.NET Core Web API demonstrating how to use the ValiBlob storage library.

## What this sample demonstrates

- DI setup with `ValiBlob.Local` for zero-credential local development
- Pipeline configuration: file validation (max 50 MB, blocked extensions) and automatic compression
- Resumable (chunked) upload sessions
- Health checks via `ValiBlob.HealthChecks`
- All CRUD operations: upload, download, delete, exists, list, metadata, copy
- Presigned URL generation (returns HTTP 501 on the local provider, works on cloud providers)
- Multi-provider resolution with `IStorageFactory`

## How to run

```bash
cd samples/ValiBlob.Sample
dotnet run
```

The API starts on `http://localhost:5000`. Open `http://localhost:5000/swagger` to explore the endpoints interactively.

Files are stored in `$TMPDIR/valiblob-sample/`.

## Available endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/files/{*path}` | Upload a file (multipart form) |
| `GET` | `/files/{*path}` | Download a file |
| `DELETE` | `/files/{*path}` | Delete a file |
| `GET` | `/files/{*path}/exists` | Check if a file exists |
| `GET` | `/files` | List files (optional `?prefix=`) |
| `GET` | `/files/{*path}/metadata` | Get file metadata |
| `POST` | `/files/{*sourcePath}/copy` | Copy a file (`?destination=`) |
| `GET` | `/files/{*path}/presigned-download` | Get a presigned download URL |
| `POST` | `/files/{*path}/presigned-upload` | Get a presigned upload URL |
| `GET` | `/providers` | List registered storage providers |
| `POST` | `/uploads/start` | Start a resumable upload session |
| `PUT` | `/uploads/{uploadId}/chunks/{offset}` | Upload a chunk |
| `GET` | `/uploads/{uploadId}/status` | Get resumable upload status |
| `POST` | `/uploads/{uploadId}/complete` | Finalize a resumable upload |
| `DELETE` | `/uploads/{uploadId}` | Abort a resumable upload |
| `GET` | `/health` | Health check endpoint |

## Switching to a real cloud provider

Replace `.UseLocal(...)` with the provider of your choice. For example, to use AWS S3:

```csharp
// 1. Add the package reference:
//    <ProjectReference Include="..\..\src\ValiBlob.AWS\ValiBlob.AWS.csproj" />

// 2. Replace .UseLocal(...) with:
.UseAws(o =>
{
    o.BucketName = "my-bucket";
    o.Region = "us-east-1";
})
```

Update `options.DefaultProvider = "AWS"` and remove the `ValiBlob.Local` project reference.
