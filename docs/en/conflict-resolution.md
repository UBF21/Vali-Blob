# Conflict Resolution

When an upload targets a path that already exists in storage, ValiBlob can handle the conflict in three ways, controlled by the `ConflictResolution` enum on `UploadRequest`.

---

## The `ConflictResolution` enum

| Value | Behaviour |
|---|---|
| `Overwrite` (default) | Replace the existing file silently. No existence check is performed. |
| `Rename` | Automatically find the next available path by appending a numeric suffix. Falls back to a GUID if all numeric candidates are taken. |
| `Fail` | Throw `StorageValidationException` if the file already exists. |

---

## Setting conflict resolution per request

```csharp
var request = new UploadRequest
{
    Path = StoragePath.From("uploads", "report.pdf"),
    Content = fileStream,
    ContentType = "application/pdf",
    ConflictResolution = ConflictResolution.Rename
};

var result = await _storage.UploadAsync(request);

// result.Value.Path may be "uploads/report_1.pdf" if "uploads/report.pdf" existed
Console.WriteLine($"Saved to: {result.Value!.Path}");
```

---

## `Overwrite` — silent replace

The default. The upload proceeds without checking whether the destination path exists. Identical to the behaviour of most cloud storage APIs.

```csharp
var request = new UploadRequest
{
    Path = StoragePath.From("avatars", userId, "profile.jpg"),
    Content = newAvatarStream,
    ContentType = "image/jpeg",
    ConflictResolution = ConflictResolution.Overwrite // or omit — same thing
};
```

Use when you want idempotent uploads (e.g., profile picture updates, file synchronization).

---

## `Rename` — automatic safe naming

The middleware appends `_1`, `_2`, ... to the filename until it finds an available path. After 1,000 attempts, a GUID is appended to guarantee uniqueness.

```
report.pdf          → exists
report_1.pdf        → exists
report_2.pdf        → available  ✓
```

GUID fallback (after 1,000 collisions):

```
report_3f1a2b4c9d8e7f6a5b3c2d1e0f9a8b7c.pdf
```

```csharp
var request = new UploadRequest
{
    Path = StoragePath.From("shared", "document.docx"),
    Content = docStream,
    ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    ConflictResolution = ConflictResolution.Rename
};

var result = await _storage.UploadAsync(request);
// Uploaded path is available in result.Value!.Path
```

Use when multiple users may upload files with the same name and you want to preserve all copies (e.g., a shared upload folder, a CMS asset library).

---

## `Fail` — explicit conflict detection

If the destination path already exists, the upload is cancelled and a `StorageValidationException` is thrown. Use this when duplicate uploads represent an error condition that your application logic must handle.

```csharp
var request = new UploadRequest
{
    Path = StoragePath.From("invoices", "INV-2026-001.pdf"),
    Content = invoiceStream,
    ContentType = "application/pdf",
    ConflictResolution = ConflictResolution.Fail
};

try
{
    var result = await _storage.UploadAsync(request);
}
catch (StorageValidationException)
{
    // Invoice with this number already exists — surface an error to the user
    return Results.Conflict(new { error = "Invoice INV-2026-001 already exists." });
}
```

Use for idempotency-sensitive operations: invoice numbers, order IDs, or any case where a duplicate upload indicates a logical error rather than a user intent.

---

## When to use each mode

| Scenario | Recommended mode |
|---|---|
| Profile picture, avatar, cover photo | `Overwrite` |
| Config file sync | `Overwrite` |
| CMS uploads where all versions must be kept | `Rename` |
| Shared folder with multiple contributors | `Rename` |
| Invoice or document with a unique identifier | `Fail` |
| Import pipeline where duplicates are bugs | `Fail` |

---

## Performance note

`Overwrite` makes no round-trip to check existence — it is the fastest mode. Both `Rename` and `Fail` issue at least one `ExistsAsync` call before the upload. `Rename` may issue up to 1,001 calls in the worst case (though this is extremely unlikely in practice). For performance-sensitive bulk uploads, prefer `Overwrite` or use `StoragePathExtensions` to generate unique paths before calling the upload.
