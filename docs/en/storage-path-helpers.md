# Storage Path Helpers

`StoragePathExtensions` provides five extension methods on `StoragePath` for common path transformations: date-based prefixes, hash suffixes, random suffixes, and sanitization. All methods are pure — they return a new `StoragePath` and do not modify the original.

---

## Methods at a glance

| Method | Example output |
|---|---|
| `WithDatePrefix()` | `2026/03/17/photo.jpg` |
| `WithTimestampPrefix()` | `2026/03/17/14-30-00/photo.jpg` |
| `WithHashSuffix(content)` | `photo_a3f2b1c4.jpg` |
| `WithRandomSuffix()` | `photo_5f3a2b1c.jpg` |
| `Sanitize()` | `my_document_v2.pdf` |

---

## `WithDatePrefix()`

Prepends a `yyyy/MM/dd` prefix based on the current UTC date.

```csharp
var path = StoragePath.From("photo.jpg").WithDatePrefix();
// → StoragePath("2026/03/17/photo.jpg")
```

Useful for organizing files by upload date, enabling efficient lifecycle policies and date-range queries.

---

## `WithTimestampPrefix()`

Prepends a `yyyy/MM/dd/HH-mm-ss` prefix based on the current UTC date and time.

```csharp
var path = StoragePath.From("export.csv").WithTimestampPrefix();
// → StoragePath("2026/03/17/14-30-00/export.csv")
```

Use when you need second-level precision — for example, to group files by the batch or job that created them, or to provide a natural sort order in chronological listings.

---

## `WithHashSuffix(content)`

Appends a short 8-character hex string derived from the SHA-256 hash of the provided `content` string.

```csharp
var path = StoragePath.From("photo.jpg").WithHashSuffix("user-42");
// → StoragePath("photo_a3f2b1c4.jpg")
```

The hash is computed from the `content` parameter (a string you supply — typically a user ID, session ID, or the file's own content hash). Only the first 4 bytes of the SHA-256 hash are used, producing an 8-character suffix.

Use to create deterministic paths: the same `content` string always produces the same suffix, which is useful for idempotent uploads or for building lookup keys.

---

## `WithRandomSuffix()`

Appends 8 random hexadecimal characters from a fresh GUID.

```csharp
var path = StoragePath.From("photo.jpg").WithRandomSuffix();
// → StoragePath("photo_5f3a2b1c.jpg")
```

Use when you need guaranteed uniqueness without the overhead of checking storage for conflicts. Each call produces a different suffix.

---

## `Sanitize()`

Normalizes a path to a safe subset of characters:

- Replaces backslashes (`\`) with forward slashes (`/`)
- Collapses consecutive slashes (`//`) into single slashes
- Replaces any character that is not alphanumeric, `-`, `_`, `.`, or `/` with `_`
- Strips leading and trailing slashes

```csharp
var path = StoragePath.From("My Documents\\Report 2026!.pdf").Sanitize();
// → StoragePath("My_Documents/Report_2026_.pdf")
```

Use when paths are constructed from user-supplied input (filenames, folder names) to prevent path traversal attacks and encoding issues.

---

## Chaining

All methods return `StoragePath`, so they can be chained in any combination:

```csharp
// Sanitize user input, then add a date prefix for organization
var path = StoragePath.From(userSuppliedFileName)
    .Sanitize()
    .WithDatePrefix();
// → "2026/03/17/my_file.pdf"

// Date prefix + random suffix for collision-proof archival
var archivePath = StoragePath.From("backup.tar.gz")
    .WithDatePrefix()
    .WithRandomSuffix();
// → "2026/03/17/backup_c2a3f1b4.tar.gz"

// Timestamp prefix for audit trail
var auditPath = StoragePath.From(fileName)
    .Sanitize()
    .WithTimestampPrefix();
// → "2026/03/17/09-15-00/invoice_12345.pdf"
```

---

## When to use each

| Scenario | Recommended method |
|---|---|
| Organize uploads by day, enable S3 lifecycle policies | `WithDatePrefix()` |
| Group files by the job or batch that produced them | `WithTimestampPrefix()` |
| Deterministic path from user ID or content fingerprint | `WithHashSuffix(userId)` |
| Avoid filename conflicts without checking storage | `WithRandomSuffix()` |
| Accept user-supplied filenames safely | `Sanitize()` |
| Accept and organize user-supplied filenames | `.Sanitize().WithDatePrefix()` |

---

## Integration with `ConflictResolution`

These helpers work well alongside `ConflictResolution.Overwrite` when you want to manage uniqueness yourself:

```csharp
var path = StoragePath.From(uploadedFileName)
    .Sanitize()
    .WithHashSuffix(userId); // deterministic per user per filename

var request = new UploadRequest
{
    Path = path,
    Content = fileStream,
    ConflictResolution = ConflictResolution.Overwrite // idempotent — same user re-uploading same filename
};
```
