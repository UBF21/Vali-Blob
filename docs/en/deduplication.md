# Deduplication

The `DeduplicationMiddleware` computes a SHA-256 hash of the file content before every upload, stores that hash in the file's metadata, and — optionally — cancels the upload when an identical file already exists in storage.

Deduplication is **opt-in**: it is disabled by default because scanning existing files has a performance cost.

---

## How it works

1. The middleware reads the entire content stream and computes its SHA-256 hash.
2. The stream is rewound to position 0 (if seekable) so subsequent middleware and the provider can read it normally.
3. The hash is stored in `context.Items["deduplication.contentHash"]` for inspection by downstream middleware.
4. The hash is stamped into the upload request's metadata under the key configured by `MetadataHashKey` (default `x-content-hash`). This makes every uploaded file discoverable by its content fingerprint.
5. When `CheckBeforeUpload` is `true`, the middleware lists all files in storage and reads their metadata looking for a file whose `x-content-hash` matches. If found, the pipeline is cancelled and the existing path is surfaced through `context.Items`.

---

## Configuration

| Property | Type | Default | Description |
|---|---|---|---|
| `Enabled` | `bool` | `false` | Opt-in: must be explicitly set to `true` |
| `CheckBeforeUpload` | `bool` | `true` | When `true`, scans for a duplicate and cancels if one exists |
| `MetadataHashKey` | `string` | `"x-content-hash"` | The metadata key where the content hash is stored |

---

## Registration

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(p => p
        .WithDeduplication(o =>
        {
            o.Enabled = true;
            o.CheckBeforeUpload = true;
            o.MetadataHashKey = "x-content-hash"; // default
        })
    );
```

---

## Handling duplicates in application code

When a duplicate is detected, the pipeline sets `context.IsCancelled = true` and returns without uploading. The `UploadAsync` result will reflect a cancelled/failed state. You can inspect the items stored on the context to get the path of the existing file:

```csharp
// The middleware populates these items on the StoragePipelineContext
// before returning. Read them from your own middleware or event handler.

if (context.Items.TryGetValue("deduplication.isDuplicate", out var isDup) && isDup is true)
{
    var existingPath = context.Items["deduplication.existingPath"] as string;
    // Redirect the user to the existing file instead of uploading a new copy
    return Results.Ok(new { path = existingPath, duplicate = true });
}
```

When `CheckBeforeUpload` is `false`, the upload proceeds regardless of existing copies. The hash is still stamped in metadata so future uploads can detect duplicates against this file.

---

## Limitations

### Scan-based detection

Duplicate detection works by listing all files and reading their metadata one by one. This is an **O(n)** operation: it issues one `GetMetadata` call per file in storage. For large buckets this can be slow and expensive.

**Recommendations:**

- Use deduplication on bounded buckets (e.g. per-user or per-tenant) rather than a single global bucket.
- Consider building a dedicated hash index in a database and implementing `CheckBeforeUpload = false` combined with a custom lookup in your application layer.
- If the storage provider supports server-side metadata querying (e.g., S3 Select, Azure Table lookups), implement a custom `IStorageMiddleware` that leverages that capability instead.

### Non-seekable streams

If the content stream is not seekable, the SHA-256 hash consumes the stream. After hashing, the stream cannot be rewound and the upload will fail. Ensure seekable streams are provided (e.g., `MemoryStream` or a `FileStream`) when using `DeduplicationMiddleware`.
