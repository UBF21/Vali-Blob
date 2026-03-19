# Storage Migration

`IStorageMigrator` copies files from one ValiBlob provider to another. It supports prefix filtering, dry runs, source deletion, skip-existing logic, and real-time progress reporting.

---

## Use cases

- Migrating from AWS S3 to Azure Blob Storage (or any provider combination)
- Moving files between buckets within the same provider
- Archiving a subset of files to cheaper storage
- Validating migration feasibility with a dry run before committing

---

## `MigrationOptions`

| Property | Type | Default | Description |
|---|---|---|---|
| `Prefix` | `string?` | `null` | Migrate only files whose path starts with this prefix. `null` = all files |
| `DryRun` | `bool` | `false` | When `true`, simulates the migration and reports results without transferring any data |
| `DeleteSourceAfterCopy` | `bool` | `false` | When `true`, deletes each file from the source after a successful copy |
| `SkipExisting` | `bool` | `true` | Skip files that already exist in the destination |
| `MaxFiles` | `int?` | `null` | Maximum number of files to process. `null` = unlimited |

---

## `MigrationResult`

| Property | Type | Description |
|---|---|---|
| `TotalFiles` | `int` | Number of files found in the source (after prefix and `MaxFiles` filtering) |
| `Migrated` | `int` | Files successfully copied (or counted, during dry run) |
| `Skipped` | `int` | Files skipped because they already existed in the destination |
| `Failed` | `int` | Files that encountered an error |
| `Errors` | `IReadOnlyList<MigrationError>` | Per-file error details (`Path` + `Reason`) |
| `Duration` | `TimeSpan` | Total elapsed time |
| `TotalBytesTransferred` | `long` | Bytes actually transferred (0 during dry run) |

---

## Basic example: migrating from AWS to Azure

Both providers must be registered in your DI container:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()    // registered as "AWS"
    .UseAzure(); // registered as "Azure"
```

Inject `IStorageMigrator` and call `MigrateAsync`:

```csharp
public class MigrationService
{
    private readonly IStorageMigrator _migrator;

    public MigrationService(IStorageMigrator migrator) => _migrator = migrator;

    public async Task MigrateToAzureAsync(CancellationToken ct)
    {
        var result = await _migrator.MigrateAsync(
            sourceProviderName: "AWS",
            destinationProviderName: "Azure",
            options: new MigrationOptions
            {
                SkipExisting = true,
                DeleteSourceAfterCopy = false // keep source until migration is verified
            },
            cancellationToken: ct);

        Console.WriteLine($"Migration complete in {result.Duration.TotalSeconds:F1}s");
        Console.WriteLine($"  Migrated : {result.Migrated}");
        Console.WriteLine($"  Skipped  : {result.Skipped}");
        Console.WriteLine($"  Failed   : {result.Failed}");
        Console.WriteLine($"  Bytes    : {result.TotalBytesTransferred:N0}");

        foreach (var error in result.Errors)
            Console.WriteLine($"  ERROR {error.Path}: {error.Reason}");
    }
}
```

---

## Progress reporting

Pass an `IProgress<MigrationProgress>` to receive per-file progress updates:

```csharp
var progress = new Progress<MigrationProgress>(p =>
{
    Console.WriteLine($"[{p.Percentage:F1}%] {p.ProcessedFiles}/{p.TotalFiles} — {p.CurrentFile}");
});

var result = await _migrator.MigrateAsync(
    sourceProviderName: "AWS",
    destinationProviderName: "Azure",
    progress: progress,
    cancellationToken: ct);
```

`MigrationProgress` properties:

| Property | Type | Description |
|---|---|---|
| `TotalFiles` | `int` | Total files to process |
| `ProcessedFiles` | `int` | Files processed so far |
| `CurrentFile` | `string` | Path of the file currently being transferred |
| `Percentage` | `double` | `ProcessedFiles / TotalFiles * 100` |

---

## Dry run workflow

Run a dry run first to validate that the migration would succeed before touching any data:

```csharp
// Step 1: dry run
var dryResult = await _migrator.MigrateAsync(
    sourceProviderName: "AWS",
    destinationProviderName: "Azure",
    options: new MigrationOptions { DryRun = true, SkipExisting = true });

Console.WriteLine($"Would migrate {dryResult.Migrated} files ({dryResult.Skipped} already exist).");

if (dryResult.Failed > 0)
{
    Console.WriteLine("Dry run detected errors — fix before proceeding.");
    return;
}

// Step 2: real migration
var result = await _migrator.MigrateAsync(
    sourceProviderName: "AWS",
    destinationProviderName: "Azure",
    options: new MigrationOptions
    {
        DryRun = false,
        SkipExisting = true,
        DeleteSourceAfterCopy = true // move semantics
    });
```

During a dry run, no downloads or uploads occur. The migrator enumerates the source, checks existence in the destination, and counts what would be transferred.

---

## Prefix filtering

Migrate only a subset of files by specifying a path prefix:

```csharp
var result = await _migrator.MigrateAsync(
    sourceProviderName: "AWS",
    destinationProviderName: "Azure",
    options: new MigrationOptions
    {
        Prefix = "invoices/2025/",  // only migrate 2025 invoices
        SkipExisting = true
    });
```

---

## Cancellation

Pass a `CancellationToken` to stop the migration gracefully. The migrator checks the token between files:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromHours(2));

var result = await _migrator.MigrateAsync(
    "AWS", "Azure",
    cancellationToken: cts.Token);
```

Files already transferred before cancellation are not rolled back.

---

## Error handling

Errors on individual files are recorded in `MigrationResult.Errors` and do not stop the overall migration (unless cancellation is triggered). After migration, inspect `result.Failed` and `result.Errors` to identify and retry failed files.

```csharp
if (result.Failed > 0)
{
    foreach (var err in result.Errors)
        logger.LogError("Migration failed for {Path}: {Reason}", err.Path, err.Reason);
}
```
