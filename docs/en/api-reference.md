# API Reference

Complete reference for all public interfaces, types, and enumerations in ValiBlob.

---

## `IStorageProvider`

**Namespace:** `ValiBlob.Core.Abstractions`

The primary abstraction. Inject this interface in your services to interact with storage.

```csharp
public interface IStorageProvider
{
    string ProviderName { get; }

    Task<StorageResult<UploadResult>> UploadAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<StorageResult<Stream>> DownloadAsync(
        DownloadRequest request,
        CancellationToken cancellationToken = default);

    Task<StorageResult> DeleteAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<StorageResult<bool>> ExistsAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<StorageResult<string>> GetUrlAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<StorageResult> CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task<StorageResult> MoveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task<StorageResult<FileMetadata>> GetMetadataAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<StorageResult> SetMetadataAsync(
        string path,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesAsync(
        string? prefix = null,
        ListOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(
        IEnumerable<StoragePath> paths,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<FileEntry> ListAllAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default);

    Task<StorageResult> DeleteFolderAsync(
        string prefix,
        CancellationToken cancellationToken = default);

    Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default);

    Task<StorageResult<UploadResult>> UploadFromUrlAsync(
        string sourceUrl,
        StoragePath destinationPath,
        string? bucketOverride = null,
        CancellationToken cancellationToken = default);
}
```

### Method notes

| Method | Notes |
|---|---|
| `UploadAsync` | `progress` is optional. `ContentLength` on the request enables accurate percentage reporting. |
| `DownloadAsync` | The returned `Stream` must be disposed by the caller. |
| `DeleteAsync` | Returns success even if the file did not exist (idempotent). |
| `ExistsAsync` | Returns `StorageResult<bool>` — `Value` is `true` if the file exists. |
| `GetUrlAsync` | Returns the CDN URL if configured, otherwise the provider-native URL. |
| `CopyAsync` | Server-side copy — does not transfer bytes through the application. |
| `MoveAsync` | Copy + delete. Not atomic on all providers. |
| `SetMetadataAsync` | On AWS S3 and OCI, requires a server-side re-copy. On Azure and Supabase, see provider notes. |
| `ListFilesAsync` | Returns a single page. Use `ListAllAsync` for complete enumeration. |
| `DeleteManyAsync` | Best-effort. Returns success even if some deletions fail — check `BatchDeleteResult.Failed`. |
| `ListAllAsync` | `IAsyncEnumerable` — auto-paginated. Supports cancellation. |
| `DeleteFolderAsync` | Deletes all objects whose key starts with `prefix`. Irreversible. |
| `UploadFromUrlAsync` | Not supported by `InMemoryStorageProvider`. |

---

## `IPresignedUrlProvider`

**Namespace:** `ValiBlob.Core.Abstractions`

Implemented by providers that support temporary signed URLs (AWS, Azure, Supabase, GCP with service account).

```csharp
public interface IPresignedUrlProvider
{
    Task<StorageResult<string>> GetPresignedUploadUrlAsync(
        string path,
        TimeSpan expiration,
        CancellationToken cancellationToken = default);

    Task<StorageResult<string>> GetPresignedDownloadUrlAsync(
        string path,
        TimeSpan expiration,
        CancellationToken cancellationToken = default);
}
```

Cast `IStorageProvider` to `IPresignedUrlProvider` at runtime:

```csharp
if (_storage is IPresignedUrlProvider presigned)
{
    var result = await presigned.GetPresignedDownloadUrlAsync(path, TimeSpan.FromHours(1));
}
```

---

## `IStorageFactory`

**Namespace:** `ValiBlob.Core.Abstractions`

Resolves providers by name or type. Useful for multi-provider scenarios where you need to select the provider at runtime.

```csharp
public interface IStorageFactory
{
    IStorageProvider Create(string? providerName = null);
    IStorageProvider Create<TProvider>() where TProvider : IStorageProvider;
    IEnumerable<IStorageProvider> GetAll();
}
```

| Method | Description |
|---|---|
| `Create(string? providerName)` | Returns the provider registered under `providerName`. If `null`, returns the default provider. Throws if not found. |
| `Create<TProvider>()` | Returns the provider of type `TProvider`. |
| `GetAll()` | Returns all registered providers. |

### Example

```csharp
public class StorageRouter
{
    private readonly IStorageFactory _factory;

    public StorageRouter(IStorageFactory factory) => _factory = factory;

    public IStorageProvider GetForRegion(string region) =>
        region switch
        {
            "us" => _factory.Create("AWS"),
            "eu" => _factory.Create("Azure"),
            _    => _factory.Create()  // default
        };
}
```

---

## `IStorageEventHandler`

**Namespace:** `ValiBlob.Core.Events`

```csharp
public interface IStorageEventHandler
{
    Task OnUploadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default);
    Task OnUploadFailedAsync(StorageEventContext context, CancellationToken cancellationToken = default);
    Task OnDownloadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default);
    Task OnDeleteCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default);
}
```

See [Event Hooks](event-hooks.md) for full documentation.

---

## `IStorageMiddleware`

**Namespace:** `ValiBlob.Core.Abstractions`

```csharp
public interface IStorageMiddleware
{
    Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next);
}

public delegate Task StorageMiddlewareDelegate(StoragePipelineContext context);
```

See [Pipeline and Middleware](pipeline.md) for full documentation.

---

## `StoragePath`

**Namespace:** `ValiBlob.Core.Models`

Immutable, typed cloud storage path. See [StoragePath](storage-path.md) for full documentation.

| Member | Signature | Description |
|---|---|---|
| `From` | `static StoragePath From(params string[] segments)` | Primary factory — creates a path from segments |
| `Append` | `StoragePath Append(string segment)` | Returns new path with segment appended |
| `Parent` | `StoragePath? Parent` | Path without last segment, or `null` if single segment |
| `FileName` | `string FileName` | Last segment |
| `Extension` | `string? Extension` | Extension of last segment including dot, or `null` |
| `Segments` | `IReadOnlyList<string> Segments` | All segments |
| `/` | `StoragePath operator /(StoragePath, string)` | Append operator |
| implicit `string` | `implicit operator string(StoragePath)` | Joins segments with `/` |
| implicit `StoragePath` | `implicit operator StoragePath(string)` | Splits string on `/` |
| `ToString` | `string ToString()` | Joins segments with `/` |
| `Equals` | `bool Equals(StoragePath?)` | Ordinal segment-by-segment equality |
| `==` / `!=` | Operators | Structural equality |

---

## `StorageResult` and `StorageResult<T>`

**Namespace:** `ValiBlob.Core.Models`

Result types returned by all storage operations. `StorageResult` is for operations with no return value (delete, copy, move). `StorageResult<T>` wraps a typed return value.

### `StorageResult`

| Member | Type | Description |
|---|---|---|
| `IsSuccess` | `bool` | `true` if the operation succeeded |
| `ErrorMessage` | `string?` | Human-readable error description, `null` on success |
| `ErrorCode` | `StorageErrorCode` | Structured error code, `None` on success |
| `Exception` | `Exception?` | Original exception if applicable |
| `Success()` | `static StorageResult` | Factory for success result |
| `Failure(...)` | `static StorageResult` | Factory for failure result |
| implicit `bool` | `implicit operator bool` | Converts to `IsSuccess` |
| `ToString()` | `string` | `"Success"` or `"Failure(code): message"` |

### `StorageResult<T>`

Inherits all members of `StorageResult` plus:

| Member | Type | Description |
|---|---|---|
| `Value` | `T?` | The operation result value, `null` on failure |
| `Success(T value)` | `static StorageResult<T>` | Factory for success result with value |
| `Failure(...)` | `static StorageResult<T>` | Factory for failure result |
| `Map<TResult>(Func<T, TResult>)` | `StorageResult<TResult>` | Transforms `Value` if success; propagates failure |

### `Map` usage

```csharp
var urlResult = await _storage.GetUrlAsync("images/photo.jpg");

// Extract only the URL string, keeping error propagation
StorageResult<Uri> uriResult = urlResult.Map(url => new Uri(url));
```

---

## `UploadRequest`

**Namespace:** `ValiBlob.Core.Models`

| Property | Type | Required | Description |
|---|---|---|---|
| `Path` | `StoragePath` | Yes | Destination path |
| `Content` | `Stream` | Yes | File content stream |
| `ContentType` | `string?` | No | MIME type (e.g., `"image/jpeg"`) |
| `ContentLength` | `long?` | No | File size in bytes — enables accurate progress reporting |
| `Metadata` | `IDictionary<string, string>?` | No | Custom key-value metadata |
| `Options` | `UploadOptions?` | No | Per-request upload options |
| `BucketOverride` | `string?` | No | Override the default bucket for this operation |

### `UploadOptions`

| Property | Type | Default | Description |
|---|---|---|---|
| `UseMultipart` | `bool` | `false` | Force multipart upload regardless of size |
| `ChunkSizeMb` | `int` | `8` | Chunk size for multipart upload |
| `Overwrite` | `bool` | `true` | Overwrite if the file already exists |
| `Encryption` | `StorageEncryptionMode` | `None` | Per-request encryption mode |

### `StorageEncryptionMode`

| Value | Description |
|---|---|
| `None` | No encryption |
| `ProviderManaged` | Cloud provider handles encryption (e.g., AWS SSE-S3) |
| `ClientSide` | ValiBlob encrypts with AES-256-CBC before upload |

### `WithContent` and `WithMetadata`

Immutable update methods that return a new `UploadRequest` with the changed property:

```csharp
var modified = request.WithContent(compressedStream);
var tagged = request.WithMetadata(new Dictionary<string, string> { ["tag"] = "value" });
```

---

## `DownloadRequest`

**Namespace:** `ValiBlob.Core.Models`

| Property | Type | Required | Description |
|---|---|---|---|
| `Path` | `StoragePath` | Yes | Path of the file to download |
| `Range` | `DownloadRange?` | No | Byte range for partial download |
| `BucketOverride` | `string?` | No | Override the default bucket for this operation |

### `DownloadRange`

| Property | Type | Description |
|---|---|---|
| `From` | `long` | Start byte offset (inclusive) |
| `To` | `long?` | End byte offset (inclusive), or `null` for end of file |

### Example — partial download

```csharp
var result = await _storage.DownloadAsync(new DownloadRequest
{
    Path = StoragePath.From("videos", "lecture.mp4"),
    Range = new DownloadRange { From = 0, To = 1024 * 1024 - 1 } // first 1 MB
});
```

---

## `UploadResult`

**Namespace:** `ValiBlob.Core.Models`

Returned by `UploadAsync` and `UploadFromUrlAsync` on success.

| Property | Type | Description |
|---|---|---|
| `Path` | `string` | The path where the file was stored |
| `ETag` | `string?` | Entity tag (MD5 hash or provider-assigned) |
| `SizeBytes` | `long` | Uploaded file size in bytes |
| `Url` | `string?` | Direct URL to the uploaded file (CDN or provider URL) |
| `UploadedAt` | `DateTimeOffset` | UTC timestamp of the upload |

---

## `FileMetadata`

**Namespace:** `ValiBlob.Core.Models`

Returned by `GetMetadataAsync`.

| Property | Type | Description |
|---|---|---|
| `Path` | `string` | Object path |
| `SizeBytes` | `long` | File size in bytes |
| `ContentType` | `string?` | MIME type |
| `LastModified` | `DateTimeOffset?` | Last modification time |
| `CreatedAt` | `DateTimeOffset?` | Creation time (if available) |
| `ETag` | `string?` | Entity tag |
| `CustomMetadata` | `IDictionary<string, string>` | User-defined metadata key-value pairs |

---

## `FileEntry`

**Namespace:** `ValiBlob.Core.Models`

Returned by `ListFilesAsync` and `ListAllAsync`.

| Property | Type | Description |
|---|---|---|
| `Path` | `string` | Object path |
| `SizeBytes` | `long` | File size in bytes |
| `ContentType` | `string?` | MIME type |
| `LastModified` | `DateTimeOffset?` | Last modification time |
| `ETag` | `string?` | Entity tag |
| `IsDirectory` | `bool` | `true` for virtual directory entries (provider-dependent) |

---

## `ListOptions`

**Namespace:** `ValiBlob.Core.Models`

Optional parameter for `ListFilesAsync` to control pagination and output.

| Property | Type | Default | Description |
|---|---|---|---|
| `MaxResults` | `int?` | `null` | Maximum number of entries to return |
| `ContinuationToken` | `string?` | `null` | Token for fetching the next page |
| `IncludeDirectories` | `bool` | `false` | Include virtual directory entries |
| `Delimiter` | `string?` | `null` | Delimiter for grouping (e.g., `"/"`) |

---

## `BatchDeleteResult`

**Namespace:** `ValiBlob.Core.Models`

| Property | Type | Description |
|---|---|---|
| `TotalRequested` | `int` | Number of paths submitted for deletion |
| `Deleted` | `int` | Number of successfully deleted files |
| `Failed` | `int` | Number of files that could not be deleted |
| `Errors` | `IReadOnlyList<BatchDeleteError>` | Details for each failed deletion |

### `BatchDeleteError`

| Property | Type | Description |
|---|---|---|
| `Path` | `string` | Path that could not be deleted |
| `Reason` | `string` | Description of why deletion failed |

---

## `UploadProgress`

**Namespace:** `ValiBlob.Core.Models`

Reported to the `IProgress<UploadProgress>` callback during uploads.

| Member | Type | Description |
|---|---|---|
| `BytesTransferred` | `long` | Bytes transferred so far |
| `TotalBytes` | `long?` | Total file size (`null` if unknown) |
| `Percentage` | `double?` | Percentage complete (0–100), `null` if total unknown |
| `ToString()` | `string` | Human-readable progress string |

```csharp
var progress = new Progress<UploadProgress>(p =>
{
    if (p.Percentage.HasValue)
        Console.Write($"\rUpload: {p.Percentage:F1}%   ");
    else
        Console.Write($"\rUploaded: {p.BytesTransferred:N0} bytes");
});

await _storage.UploadAsync(request, progress);
```

---

## `StorageErrorCode`

**Namespace:** `ValiBlob.Core.Models`

| Value | Description |
|---|---|
| `None` | No error — operation succeeded |
| `FileNotFound` | The requested file does not exist |
| `AccessDenied` | Insufficient permissions for the operation |
| `QuotaExceeded` | Storage quota or rate limit exceeded |
| `NetworkError` | Transient network connectivity issue |
| `ValidationFailed` | File rejected by the validation middleware |
| `ProviderError` | Generic provider-level error (catch-all) |
| `Timeout` | Operation exceeded the configured timeout |
| `NotSupported` | Operation is not supported by this provider |
| `Conflict` | Conflict with existing resource (e.g., overwrite disabled and file exists) |

---

## `StorageEventContext`

**Namespace:** `ValiBlob.Core.Events`

| Property | Type | Description |
|---|---|---|
| `ProviderName` | `string` | Name of the provider that handled the operation |
| `OperationType` | `string` | `"Upload"`, `"Download"`, `"Delete"`, etc. |
| `Path` | `string?` | Affected object path |
| `IsSuccess` | `bool` | Whether the operation succeeded |
| `ErrorMessage` | `string?` | Error message if `IsSuccess` is `false` |
| `ErrorCode` | `StorageErrorCode` | Structured error code |
| `Duration` | `TimeSpan` | Operation elapsed time |
| `FileSizeBytes` | `long?` | File size (upload/download events) |
| `Extra` | `IDictionary<string, object>` | Additional context data |

---

## `StoragePipelineContext`

**Namespace:** `ValiBlob.Core.Pipeline`

Passed through the middleware pipeline for each upload.

| Property | Type | Description |
|---|---|---|
| `Request` | `UploadRequest` | The current upload request; middleware can replace it |
| `Items` | `IDictionary<string, object>` | Shared data bag for middleware communication |
| `IsCancelled` | `bool` | Set to `true` to abort the upload |
| `CancellationReason` | `string?` | Human-readable reason for cancellation |

---

## `ValidationResult`

**Namespace:** `ValiBlob.Core.Abstractions`

Returned by `IFileValidator.ValidateAsync`.

| Member | Type | Description |
|---|---|---|
| `IsValid` | `bool` | `true` when `Errors.Count == 0` |
| `Errors` | `IReadOnlyList<string>` | Validation error messages |
| `Success()` | `static ValidationResult` | Factory for a valid result |
| `Failure(params string[])` | `static ValidationResult` | Factory for an invalid result with messages |
