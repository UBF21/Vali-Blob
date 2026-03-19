# StoragePath

`StoragePath` is a typed value object that represents a cloud storage object path as an ordered sequence of segments joined by `/`. It replaces raw string path manipulation and eliminates an entire class of subtle bugs.

---

## Why StoragePath exists

Working with raw strings for cloud storage paths causes recurring problems:

```csharp
// Bug: double slash produces "documents//invoices/file.pdf"
var path = "documents/" + "/" + "invoices/file.pdf";

// Bug: missing slash produces "documentsinvoices/file.pdf"
var path = "documents" + "invoices/file.pdf";

// Bug: OS path separator on Windows produces "documents\invoices\file.pdf"
var path = Path.Combine("documents", "invoices", "file.pdf");

// Bug: hard to extract filename or parent without string parsing
var fileName = path.Split('/').Last(); // fragile
```

`StoragePath` solves all of these:

- Segments are stored as a typed array — no manual string concatenation
- The `/` operator composes paths safely
- `FileName`, `Extension`, and `Parent` are first-class properties
- Equality comparison is segment-level, not string-level
- Implicit conversions to/from `string` mean it works anywhere a `string` is expected

---

## Creating a StoragePath

### `StoragePath.From(params string[] segments)`

The primary factory method. Accepts one or more segments.

```csharp
// Multiple segments — most readable
var path = StoragePath.From("documents", "invoices", "2024", "invoice-001.pdf");
// Result: "documents/invoices/2024/invoice-001.pdf"

// Single pre-joined string — also valid
var path = StoragePath.From("documents/invoices/2024/invoice-001.pdf");
// Result: "documents/invoices/2024/invoice-001.pdf"

// Mixed — segments can themselves contain slashes
var path = StoragePath.From("documents/invoices", "2024", "invoice.pdf");
// Result: "documents/invoices/2024/invoice.pdf"
```

Empty and whitespace-only segments are silently skipped during cleaning:

```csharp
var path = StoragePath.From("documents", "", "  ", "file.pdf");
// Result: "documents/file.pdf"
```

Passing all-empty input throws `ArgumentException`:

```csharp
StoragePath.From("", "  "); // throws ArgumentException
```

### Implicit conversion from `string`

```csharp
StoragePath path = "documents/invoices/file.pdf";
// Equivalent to StoragePath.From("documents/invoices/file.pdf")
```

This means any method that accepts `StoragePath` can be called with a plain string:

```csharp
await _storage.DeleteAsync("documents/old-file.pdf"); // string implicitly becomes StoragePath
```

---

## The `/` operator

Append segments with the `/` operator for fluent, readable path construction:

```csharp
var base = StoragePath.From("uploads");
var path = base / "images" / "2024" / "photo.jpg";
// Result: "uploads/images/2024/photo.jpg"
```

Combine with variables:

```csharp
var tenantId = "tenant-abc";
var year = DateTime.UtcNow.Year.ToString();
var fileName = "report.xlsx";

var path = StoragePath.From("tenants") / tenantId / "reports" / year / fileName;
// Result: "tenants/tenant-abc/reports/2024/report.xlsx"
```

---

## `Append` method

Functionally equivalent to the `/` operator but called as a method:

```csharp
var path = StoragePath.From("documents").Append("invoices").Append("file.pdf");
// Result: "documents/invoices/file.pdf"
```

---

## Properties

### `FileName`

Returns the last segment — the "file name" portion of the path.

```csharp
var path = StoragePath.From("documents", "invoices", "invoice-001.pdf");
Console.WriteLine(path.FileName); // "invoice-001.pdf"
```

### `Extension`

Returns the extension of the last segment including the leading dot, or `null` if there is no dot.

```csharp
var path = StoragePath.From("documents", "file.pdf");
Console.WriteLine(path.Extension); // ".pdf"

var pathNoExt = StoragePath.From("documents", "README");
Console.WriteLine(pathNoExt.Extension); // null
```

### `Parent`

Returns a new `StoragePath` containing all segments except the last, or `null` when the path has only one segment.

```csharp
var path = StoragePath.From("documents", "invoices", "file.pdf");
Console.WriteLine(path.Parent);         // "documents/invoices"
Console.WriteLine(path.Parent!.Parent); // "documents"
Console.WriteLine(path.Parent!.Parent!.Parent); // null
```

### `Segments`

Returns all segments as `IReadOnlyList<string>`.

```csharp
var path = StoragePath.From("a", "b", "c");
Console.WriteLine(path.Segments.Count); // 3
Console.WriteLine(path.Segments[0]);    // "a"
```

---

## Implicit conversion to `string`

`StoragePath` converts to `string` automatically wherever a string is expected.

```csharp
var path = StoragePath.From("docs", "file.pdf");

string asString = path;           // "docs/file.pdf"
Console.WriteLine(path);          // "docs/file.pdf" (ToString())
string.Format("{0}", path);       // "docs/file.pdf"
```

---

## Equality

Two `StoragePath` instances are equal when they have the same number of segments and each segment matches using ordinal (case-sensitive) comparison.

```csharp
var a = StoragePath.From("docs", "file.pdf");
var b = StoragePath.From("docs", "file.pdf");
var c = StoragePath.From("docs", "File.pdf"); // different case

Console.WriteLine(a == b);        // true
Console.WriteLine(a == c);        // false
Console.WriteLine(a.Equals(b));   // true
Console.WriteLine(a != c);        // true
```

Use `StoragePath` as dictionary keys or in `HashSet<T>` — `GetHashCode` is consistent with `Equals`.

---

## Real-world examples

### Dated file paths

```csharp
var today = DateTimeOffset.UtcNow;
var path = StoragePath.From(
    "reports",
    today.Year.ToString(),
    today.Month.ToString("D2"),
    today.Day.ToString("D2"),
    $"daily-summary-{today:yyyyMMdd}.csv");
// Result: "reports/2024/03/15/daily-summary-20240315.csv"
```

### Tenant-isolated paths

```csharp
public StoragePath BuildTenantPath(string tenantId, string category, string fileName)
{
    return StoragePath.From("tenants") / tenantId / category / fileName;
}

var path = BuildTenantPath("acme-corp", "invoices", "inv-0042.pdf");
// Result: "tenants/acme-corp/invoices/inv-0042.pdf"
```

### User avatar with guaranteed extension

```csharp
public StoragePath AvatarPath(string userId, string originalFileName)
{
    var tempPath = StoragePath.From(originalFileName);
    var ext = tempPath.Extension ?? ".jpg"; // fallback extension
    return StoragePath.From("avatars", userId, $"profile{ext}");
}
```

### Preserving folder structure on upload

```csharp
// Given a folder of local files, mirror the structure in cloud storage
foreach (var localFile in Directory.EnumerateFiles("./exports", "*", SearchOption.AllDirectories))
{
    var relativePath = Path.GetRelativePath("./exports", localFile);
    // Normalize OS separator to forward slash
    var segments = relativePath.Replace('\\', '/').Split('/');
    var cloudPath = StoragePath.From("backups") / string.Join("/", segments);

    await _storage.UploadAsync(new UploadRequest
    {
        Path = cloudPath,
        Content = File.OpenRead(localFile)
    });
}
```

---

## Anti-patterns to avoid

### Do not use `Path.Combine` for cloud paths

```csharp
// Wrong — uses OS directory separator, breaks on Windows
var bad = Path.Combine("documents", "invoices", "file.pdf"); // "documents\invoices\file.pdf" on Windows

// Correct
var good = StoragePath.From("documents", "invoices", "file.pdf");
```

### Do not concatenate raw strings

```csharp
// Wrong — easy to introduce double slashes or missing slashes
var bad = folderName + "/" + subFolder + "/" + fileName;

// Correct
var good = StoragePath.From(folderName, subFolder, fileName);
```

### Do not hardcode leading or trailing slashes

```csharp
// Wrong — StoragePath does not strip leading/trailing slashes in a way you might expect
var bad = StoragePath.From("/documents/", "file.pdf");

// Correct — no leading or trailing slashes needed
var good = StoragePath.From("documents", "file.pdf");
```

### Do not mutate — StoragePath is immutable

`Append` and the `/` operator always return a new instance. The original is unchanged.

```csharp
var base = StoragePath.From("uploads");
var sub = base / "images"; // base is still "uploads"
Console.WriteLine(base); // "uploads"
Console.WriteLine(sub);  // "uploads/images"
```
