# Batch Operations

ValiBlob provides several operations designed for working with multiple files at once or with large collections of files. These operations are available on every provider implementation.

---

## `DeleteManyAsync`

Delete multiple files in a single call. Returns a `BatchDeleteResult` summarizing how many files were deleted and which ones failed.

### Signature

```csharp
Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(
    IEnumerable<StoragePath> paths,
    CancellationToken cancellationToken = default)
```

### `BatchDeleteResult` properties

| Property | Type | Description |
|---|---|---|
| `TotalRequested` | `int` | Number of paths passed to the method |
| `Deleted` | `int` | Number of files successfully deleted |
| `Failed` | `int` | Number of files that could not be deleted |
| `Errors` | `IReadOnlyList<BatchDeleteError>` | Details about each failed deletion |

`BatchDeleteError` has `Path` (the path that failed) and `Reason` (the error message).

### Example — delete a user's uploaded files

```csharp
public async Task DeleteUserFilesAsync(string userId, IEnumerable<string> filePaths)
{
    var paths = filePaths.Select(p => StoragePath.From(p)).ToList();

    var result = await _storage.DeleteManyAsync(paths);

    if (!result.IsSuccess)
        throw new Exception($"Batch delete failed: {result.ErrorMessage}");

    var summary = result.Value!;
    Console.WriteLine($"Deleted {summary.Deleted}/{summary.TotalRequested} files.");

    if (summary.Failed > 0)
    {
        foreach (var error in summary.Errors)
        {
            Console.WriteLine($"  Failed: {error.Path} — {error.Reason}");
        }
    }
}
```

### Partial failure handling

`DeleteManyAsync` is a best-effort operation. Even if some deletions fail, the method returns `StorageResult.Success` with the `BatchDeleteResult`. Check `result.Value!.Failed > 0` to detect partial failures.

```csharp
var result = await _storage.DeleteManyAsync(paths);

if (result.IsSuccess && result.Value!.Failed > 0)
{
    // Some deletions failed — log them and decide whether to retry
    var failedPaths = result.Value.Errors.Select(e => e.Path).ToList();
    _logger.LogWarning("Failed to delete {Count} files: {Paths}",
        failedPaths.Count, string.Join(", ", failedPaths));
}
```

---

## `ListAllAsync`

Stream all files matching an optional prefix as an `IAsyncEnumerable<FileEntry>`. Unlike `ListFilesAsync`, which returns a single page, `ListAllAsync` automatically handles pagination and yields entries one by one — keeping memory usage constant regardless of the total file count.

### Signature

```csharp
IAsyncEnumerable<FileEntry> ListAllAsync(
    string? prefix = null,
    CancellationToken cancellationToken = default)
```

### Example — iterate all files

```csharp
await foreach (var entry in _storage.ListAllAsync("documents/"))
{
    Console.WriteLine($"{entry.Path} ({entry.SizeBytes:N0} bytes)");
}
```

### Example — collect to a list (caution for large buckets)

```csharp
var allFiles = new List<FileEntry>();
await foreach (var entry in _storage.ListAllAsync(prefix: "invoices/2024/"))
    allFiles.Add(entry);
```

### Example — with cancellation

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

await foreach (var entry in _storage.ListAllAsync(cancellationToken: cts.Token))
{
    await ProcessFileAsync(entry);
}
```

### Example — LINQ with `System.Linq.Async`

```bash
dotnet add package System.Linq.Async
```

```csharp
using System.Linq;

var largeFiles = await _storage.ListAllAsync("uploads/")
    .Where(f => f.SizeBytes > 10 * 1024 * 1024)
    .ToListAsync();
```

> **⚠️ Warning:** Calling `.ToListAsync()` on a very large bucket loads all file metadata into memory. Prefer streaming iteration with `await foreach` and process entries individually to keep memory usage bounded.

---

## `DeleteFolderAsync`

Delete all files whose path starts with a given prefix. Equivalent to deleting an entire "virtual folder".

### Signature

```csharp
Task<StorageResult> DeleteFolderAsync(
    string prefix,
    CancellationToken cancellationToken = default)
```

### Example — delete a tenant's folder

```csharp
var result = await _storage.DeleteFolderAsync($"tenants/{tenantId}/");

if (!result.IsSuccess)
    throw new Exception($"Folder deletion failed: {result.ErrorMessage}");
```

The trailing slash is important. `"reports/2023"` matches `"reports/2023-backup"` as well as `"reports/2023/january.csv"`. Use `"reports/2023/"` to scope to the `2023` folder only.

> **⚠️ Warning:** `DeleteFolderAsync` is irreversible. There is no soft delete or recycle bin in any provider. Ensure you have the correct prefix before calling this method.

### Confirming deletion

```csharp
await _storage.DeleteFolderAsync("temp/processing/");

// Verify no files remain
var remaining = await _storage.ListFilesAsync("temp/processing/");
if (remaining.IsSuccess && remaining.Value!.Count > 0)
    Console.WriteLine($"Warning: {remaining.Value.Count} files still present.");
```

---

## `ListFoldersAsync`

Returns the unique top-level "virtual folder" names under an optional prefix. Cloud storage is a flat key-value store, so "folders" are derived from path segments.

### Signature

```csharp
Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(
    string? prefix = null,
    CancellationToken cancellationToken = default)
```

### Example — list root-level folders

```csharp
var result = await _storage.ListFoldersAsync();

if (result.IsSuccess)
{
    foreach (var folder in result.Value!)
        Console.WriteLine(folder);
    // e.g.: "documents", "images", "reports", "tenants"
}
```

### Example — list sub-folders

```csharp
var result = await _storage.ListFoldersAsync("documents/");

foreach (var folder in result.Value!)
    Console.WriteLine(folder);
// e.g.: "invoices", "contracts", "receipts"
```

---

## `UploadFromUrlAsync`

Upload a file directly from a remote URL to cloud storage without routing the bytes through your server. The provider fetches the URL and writes the content to the destination path.

### Signature

```csharp
Task<StorageResult<UploadResult>> UploadFromUrlAsync(
    string sourceUrl,
    StoragePath destinationPath,
    string? bucketOverride = null,
    CancellationToken cancellationToken = default)
```

### Example — save a user's avatar from a third-party URL

```csharp
public async Task<string> SaveAvatarFromUrlAsync(string imageUrl, string userId)
{
    var destination = StoragePath.From("avatars", userId, "profile.jpg");

    var result = await _storage.UploadFromUrlAsync(
        sourceUrl: imageUrl,
        destinationPath: destination);

    if (!result.IsSuccess)
        throw new Exception($"Remote upload failed: {result.ErrorMessage}");

    return result.Value!.Path;
}
```

### Example — mirror an asset to a tenant bucket

```csharp
await _storage.UploadFromUrlAsync(
    sourceUrl: "https://cdn.example.com/assets/logo.png",
    destinationPath: StoragePath.From("branding", "logo.png"),
    bucketOverride: $"tenant-{tenantId}");
```

> **💡 Tip:** `UploadFromUrlAsync` prevents your server from acting as a proxy for large files. The bytes flow directly between the remote server and the cloud storage provider (where the provider supports it), saving bandwidth and reducing latency.

> **⚠️ Warning:** The `InMemoryStorageProvider` returns `StorageErrorCode.NotSupported` for `UploadFromUrlAsync`. Test this operation against a real provider or a local MinIO instance.

---

## Performance tips

### Parallelizing batch uploads

When uploading many small files, parallelize with a degree of concurrency appropriate for your environment:

```csharp
var semaphore = new SemaphoreSlim(8); // max 8 concurrent uploads

var tasks = filePaths.Select(async filePath =>
{
    await semaphore.WaitAsync();
    try
    {
        using var stream = File.OpenRead(filePath);
        await _storage.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("batch", Path.GetFileName(filePath)),
            Content = stream
        });
    }
    finally
    {
        semaphore.Release();
    }
});

await Task.WhenAll(tasks);
```

### Using `ListAllAsync` for large catalogs

Prefer `ListAllAsync` over `ListFilesAsync` for catalogs with more than a few thousand files. `ListFilesAsync` is limited to a single page; `ListAllAsync` streams the entire result set without holding all entries in memory simultaneously.

### Batching deletes

Cloud providers typically have lower API call costs per object for bulk deletes than individual deletes. AWS S3 supports native batch delete (up to 1000 objects per request). `DeleteManyAsync` leverages this natively — always prefer it over calling `DeleteAsync` in a loop.
