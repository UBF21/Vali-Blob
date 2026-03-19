# Resumable Uploads

ValiBlob supports resumable (chunked) uploads for large files through the `IResumableUploadProvider` interface. Each provider uses its native mechanism under the hood while exposing a single, consistent API.

---

## Provider support matrix

| Provider | Mechanism | Truly resumable? |
|---|---|---|
| **AWS S3** | S3 Multipart Upload API | Yes — state managed by S3 |
| **Azure Blob** | Block Blobs (`StageBlockAsync`) | Yes — staged blocks persist 7 days |
| **Supabase** | Native TUS 1.0.0 protocol | Yes — full TUS semantics |
| **GCP** | Temp-file buffered + atomic upload | Partial — chunks persist on local disk; process restart loses the buffer |
| **OCI** | OCI Multipart Upload API | Yes — similar to AWS |
| **InMemory** | In-process sorted chunk buffer | Yes — for testing |

> **GCP note:** The Google Cloud Storage .NET SDK does not expose the internal resumable-upload URI publicly. ValiBlob buffers chunks in a pre-allocated temporary file and uploads the complete file atomically on `CompleteResumableUploadAsync`. For truly distributed resumable uploads on GCP, consider implementing `IResumableSessionStore` with persistent storage and a custom GCS REST client.

---

## The 5-method lifecycle

```
StartResumableUploadAsync   → creates a session, returns UploadId
      │
      ↓  (repeat for each chunk)
UploadChunkAsync            → sends a chunk at a given byte offset
      │
      ↓  (optional — to resume after an interruption)
GetUploadStatusAsync        → returns bytes received so far
      │
      ↓
CompleteResumableUploadAsync → finalizes the file at the provider
      │
      └──── AbortResumableUploadAsync  (call if you want to cancel instead)
```

---

## Configuration

### Global options (appsettings.json)

```json
{
  "ValiStorage:ResumableUpload": {
    "DefaultChunkSizeBytes": 8388608,
    "MinPartSizeBytes": 5242880,
    "SessionExpiration": "24:00:00",
    "EnableChecksumValidation": true,
    "MaxConcurrentChunks": 1
  }
}
```

| Property | Default | Description |
|---|---|---|
| `DefaultChunkSizeBytes` | 8 MB | Default chunk size when no per-request override is given |
| `MinPartSizeBytes` | 5 MB | Minimum part size. AWS S3 and OCI require ≥ 5 MB for non-final parts |
| `SessionExpiration` | 24 h | How long a session remains valid in the store |
| `EnableChecksumValidation` | `true` | Validate MD5 checksums per chunk when the provider supports it |
| `MaxConcurrentChunks` | 1 | Max parallel chunks (where supported). `1` = strictly sequential |

### Code-based configuration

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithResumableUploads(o =>
    {
        o.DefaultChunkSizeBytes = 10 * 1024 * 1024; // 10 MB chunks
        o.SessionExpiration = TimeSpan.FromHours(48);
        o.EnableChecksumValidation = true;
    });
```

### Per-request override

```csharp
var request = new ResumableUploadRequest
{
    Path = StoragePath.From("videos", "lecture.mp4"),
    TotalSize = fileSize,
    Options = new ResumableUploadRequestOptions
    {
        ChunkSizeBytes = 16 * 1024 * 1024,        // 16 MB for this file
        SessionExpiration = TimeSpan.FromHours(6)  // shorter expiration
    }
};
```

---

## Basic usage

### 1. Start a session

```csharp
var provider = serviceProvider.GetRequiredKeyedService<IStorageProvider>("AWS")
    as IResumableUploadProvider;

var startResult = await provider.StartResumableUploadAsync(new ResumableUploadRequest
{
    Path = StoragePath.From("uploads", "videos", "conference.mp4"),
    ContentType = "video/mp4",
    TotalSize = fileInfo.Length,
    Metadata = new Dictionary<string, string>
    {
        ["uploaded-by"] = userId,
        ["original-name"] = fileName
    }
});

if (!startResult.IsSuccess)
    return Problem(startResult.ErrorMessage);

var uploadId = startResult.Value!.UploadId;
// Store uploadId so the client can resume from the same upload later
```

### 2. Upload chunks

```csharp
const long chunkSize = 8 * 1024 * 1024; // 8 MB
using var fileStream = File.OpenRead(localPath);

long offset = 0;
while (offset < fileInfo.Length)
{
    var remaining = fileInfo.Length - offset;
    var currentChunkSize = Math.Min(chunkSize, remaining);

    var buffer = new byte[currentChunkSize];
    var bytesRead = await fileStream.ReadAsync(buffer, 0, (int)currentChunkSize);
    if (bytesRead == 0) break;

    var chunkResult = await provider.UploadChunkAsync(new ResumableChunkRequest
    {
        UploadId = uploadId,
        Data = new MemoryStream(buffer, 0, bytesRead),
        Offset = offset,
        Length = bytesRead
    });

    if (!chunkResult.IsSuccess)
        return Problem($"Chunk at offset {offset} failed: {chunkResult.ErrorMessage}");

    Console.WriteLine($"Progress: {chunkResult.Value!.ProgressPercent:F1}%");
    offset += bytesRead;
}
```

### 3. Complete the upload

```csharp
var completeResult = await provider.CompleteResumableUploadAsync(uploadId);

if (!completeResult.IsSuccess)
    return Problem(completeResult.ErrorMessage);

Console.WriteLine($"Upload complete: {completeResult.Value!.Path} ({completeResult.Value.SizeBytes:N0} bytes)");
```

---

## Resuming an interrupted upload

Use `GetUploadStatusAsync` to query how many bytes the provider has received, then resume from that offset:

```csharp
// Called when a previous upload was interrupted
public async Task ResumeUploadAsync(string uploadId, string localFilePath)
{
    var statusResult = await provider.GetUploadStatusAsync(uploadId);
    if (!statusResult.IsSuccess)
    {
        // Session expired or doesn't exist — start over
        await StartFreshUploadAsync(localFilePath);
        return;
    }

    var status = statusResult.Value!;
    if (status.IsComplete)
    {
        Console.WriteLine("Upload was already completed.");
        return;
    }

    Console.WriteLine($"Resuming from byte {status.BytesUploaded} of {status.TotalSize} ({status.ProgressPercent:F1}%)");

    // Resume uploading from the confirmed offset
    using var fileStream = File.OpenRead(localFilePath);
    fileStream.Seek(status.BytesUploaded, SeekOrigin.Begin);

    await UploadRemainingChunksAsync(provider, uploadId, fileStream, status.BytesUploaded, status.TotalSize);

    await provider.CompleteResumableUploadAsync(uploadId);
}
```

---

## Aborting an upload

```csharp
// Frees resources at the provider (multipart ID, staged blocks, TUS session, temp file)
var result = await provider.AbortResumableUploadAsync(uploadId);
if (result.IsSuccess)
    Console.WriteLine("Upload aborted and resources freed.");
```

Always call `AbortResumableUploadAsync` when you no longer intend to complete an upload — this frees cloud-side resources and billed storage.

---

## Session store

By default, ValiBlob uses an in-memory session store (`InMemoryResumableSessionStore`). Sessions are stored in a `ConcurrentDictionary` and are **lost on process restart**.

### When to use a custom store

- **Multiple application instances** (horizontal scaling, load balancing)
- **Long-lived uploads** that may outlive a single process
- **Audit requirements** — persistent upload log

### Implementing a custom store

```csharp
public sealed class RedisResumableSessionStore : IResumableSessionStore
{
    private readonly IDatabase _db;

    public RedisResumableSessionStore(IConnectionMultiplexer redis)
        => _db = redis.GetDatabase();

    public async Task SaveAsync(ResumableUploadSession session, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(session);
        var expiry = session.ExpiresAt.HasValue
            ? session.ExpiresAt.Value - DateTimeOffset.UtcNow
            : TimeSpan.FromHours(24);
        await _db.StringSetAsync($"valiblob:session:{session.UploadId}", json, expiry);
    }

    public async Task<ResumableUploadSession?> GetAsync(string uploadId, CancellationToken ct = default)
    {
        var json = await _db.StringGetAsync($"valiblob:session:{uploadId}");
        return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<ResumableUploadSession>(json!);
    }

    public Task UpdateAsync(ResumableUploadSession session, CancellationToken ct = default)
        => SaveAsync(session, ct);

    public async Task DeleteAsync(string uploadId, CancellationToken ct = default)
        => await _db.KeyDeleteAsync($"valiblob:session:{uploadId}");
}
```

Register it:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .UseResumableSessionStore<RedisResumableSessionStore>();
```

---

## ASP.NET Core controller example

A complete HTTP API for chunked file uploads:

```csharp
[ApiController]
[Route("api/uploads")]
public class ResumableUploadController : ControllerBase
{
    private readonly IResumableUploadProvider _provider;

    public ResumableUploadController(
        [FromKeyedServices("AWS")] IStorageProvider provider)
    {
        _provider = (IResumableUploadProvider)provider;
    }

    // POST /api/uploads/start
    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartUploadDto dto)
    {
        var result = await _provider.StartResumableUploadAsync(new ResumableUploadRequest
        {
            Path = StoragePath.From("uploads", dto.FileName),
            ContentType = dto.ContentType,
            TotalSize = dto.TotalSize
        });

        if (!result.IsSuccess) return Problem(result.ErrorMessage);

        return Ok(new { uploadId = result.Value!.UploadId });
    }

    // PATCH /api/uploads/{uploadId}/chunk
    [HttpPatch("{uploadId}/chunk")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB max per chunk
    public async Task<IActionResult> UploadChunk(
        string uploadId,
        [FromHeader(Name = "Upload-Offset")] long offset)
    {
        var result = await _provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = uploadId,
            Data = Request.Body,
            Offset = offset,
            Length = Request.ContentLength
        });

        if (!result.IsSuccess) return Problem(result.ErrorMessage);

        return Ok(new
        {
            bytesUploaded = result.Value!.BytesUploaded,
            progressPercent = result.Value.ProgressPercent,
            isReadyToComplete = result.Value.IsReadyToComplete
        });
    }

    // HEAD /api/uploads/{uploadId}/status
    [HttpHead("{uploadId}/status")]
    [HttpGet("{uploadId}/status")]
    public async Task<IActionResult> GetStatus(string uploadId)
    {
        var result = await _provider.GetUploadStatusAsync(uploadId);
        if (!result.IsSuccess) return NotFound(result.ErrorMessage);

        Response.Headers["Upload-Offset"] = result.Value!.BytesUploaded.ToString();
        Response.Headers["Upload-Length"] = result.Value.TotalSize.ToString();

        return Ok(result.Value);
    }

    // POST /api/uploads/{uploadId}/complete
    [HttpPost("{uploadId}/complete")]
    public async Task<IActionResult> Complete(string uploadId)
    {
        var result = await _provider.CompleteResumableUploadAsync(uploadId);
        if (!result.IsSuccess) return Problem(result.ErrorMessage);

        return Ok(new { path = result.Value!.Path, sizeBytes = result.Value.SizeBytes });
    }

    // DELETE /api/uploads/{uploadId}
    [HttpDelete("{uploadId}")]
    public async Task<IActionResult> Abort(string uploadId)
    {
        var result = await _provider.AbortResumableUploadAsync(uploadId);
        if (!result.IsSuccess) return Problem(result.ErrorMessage);
        return NoContent();
    }
}

public record StartUploadDto(string FileName, string ContentType, long TotalSize);
```

---

## Testing resumable uploads

`InMemoryStorageProvider` implements `IResumableUploadProvider` with a sorted chunk buffer. No cloud credentials needed.

```csharp
public class VideoUploadTests
{
    private readonly InMemoryStorageProvider _provider;

    public VideoUploadTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddValiBlob().UseInMemory();
        _provider = services.BuildServiceProvider()
            .GetRequiredService<InMemoryStorageProvider>();
    }

    [Fact]
    public async Task ChunkedUpload_ShouldReassembleFileCorrectly()
    {
        var content = new byte[15 * 1024 * 1024]; // 15 MB
        new Random(42).NextBytes(content);

        // Start
        var session = (await _provider.StartResumableUploadAsync(new ResumableUploadRequest
        {
            Path = StoragePath.From("videos", "test.mp4"),
            ContentType = "video/mp4",
            TotalSize = content.Length
        })).Value!;

        // Upload 3 × 5 MB chunks
        const int chunkSize = 5 * 1024 * 1024;
        for (int i = 0; i < 3; i++)
        {
            var chunk = content.Skip(i * chunkSize).Take(chunkSize).ToArray();
            await _provider.UploadChunkAsync(new ResumableChunkRequest
            {
                UploadId = session.UploadId,
                Data = new MemoryStream(chunk),
                Offset = i * chunkSize,
                Length = chunkSize
            });
        }

        // Complete
        var result = await _provider.CompleteResumableUploadAsync(session.UploadId);
        result.IsSuccess.Should().BeTrue();

        // Verify
        var stored = _provider.GetRawBytes("videos/test.mp4");
        stored.Should().BeEquivalentTo(content);
    }
}
```

---

## Models reference

### `ResumableUploadRequest`

| Property | Type | Description |
|---|---|---|
| `Path` | `StoragePath` | Destination path (required) |
| `ContentType` | `string?` | MIME type |
| `TotalSize` | `long` | Total file size in bytes |
| `Metadata` | `IDictionary<string, string>?` | Custom metadata |
| `BucketOverride` | `string?` | Per-request bucket override |
| `Options` | `ResumableUploadRequestOptions?` | Per-request option overrides |

### `ResumableChunkRequest`

| Property | Type | Description |
|---|---|---|
| `UploadId` | `string` | Session ID from `StartResumableUploadAsync` |
| `Data` | `Stream` | Chunk data (read from current position) |
| `Offset` | `long` | Byte offset in the full file (zero-based) |
| `Length` | `long?` | Chunk length. If null, reads until end of stream |

### `ChunkUploadResult`

| Property | Type | Description |
|---|---|---|
| `UploadId` | `string` | Session ID |
| `BytesUploaded` | `long` | Total bytes confirmed by the provider |
| `TotalSize` | `long` | Declared total file size |
| `IsReadyToComplete` | `bool` | True when `BytesUploaded >= TotalSize` |
| `ProgressPercent` | `double` | Completion percentage (0–100) |

### `ResumableUploadStatus`

| Property | Type | Description |
|---|---|---|
| `UploadId` | `string` | Session ID |
| `Path` | `string` | Destination path |
| `TotalSize` | `long` | Declared total size |
| `BytesUploaded` | `long` | Bytes confirmed by the provider |
| `IsComplete` | `bool` | Upload was completed |
| `IsAborted` | `bool` | Upload was aborted |
| `ExpiresAt` | `DateTimeOffset?` | Session expiration time |
| `ProgressPercent` | `double` | Completion percentage (0–100) |

### `ResumableUploadSession`

Returned by `StartResumableUploadAsync`. Contains the `UploadId` needed for all subsequent calls. Store it client-side to support resumption after interruption.

---

## Error codes

| `StorageErrorCode` | When returned |
|---|---|
| `FileNotFound` | `UploadId` not found or session expired |
| `ValidationFailed` | Attempted operation on an aborted session |
| `ProviderError` | Provider-side error (network, auth, quota) |
| `NotSupported` | Provider does not support resumable uploads |
