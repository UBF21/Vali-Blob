# Virus Scanning

ValiBlob provides a `VirusScanMiddleware` that integrates antivirus scanning into the upload pipeline. The scanner implementation is decoupled behind the `IVirusScanner` interface so you can plug in any engine — ClamAV, Windows Defender, a commercial API, etc.

---

## The `IVirusScanner` interface

```csharp
public interface IVirusScanner
{
    Task<VirusScanResult> ScanAsync(
        Stream content,
        string? fileName,
        CancellationToken cancellationToken = default);
}
```

`ScanAsync` receives the raw content stream and the file name (for context) and returns a `VirusScanResult`:

| Property | Type | Description |
|---|---|---|
| `IsClean` | `bool` | `true` if no threat was found |
| `ThreatName` | `string?` | Name of the detected threat (when `IsClean = false`) |
| `ScannerName` | `string?` | Identifier of the scanner that produced the result |

Helper factory methods:

```csharp
VirusScanResult.Clean("MyScanner")
VirusScanResult.Infected("Trojan.GenericKD", "MyScanner")
```

---

## `NoOpVirusScanner` — the default

Out of the box, ValiBlob registers `NoOpVirusScanner` as the `IVirusScanner` implementation. It approves every file unconditionally:

```csharp
public sealed class NoOpVirusScanner : IVirusScanner
{
    public Task<VirusScanResult> ScanAsync(
        Stream content, string? fileName, CancellationToken cancellationToken = default)
        => Task.FromResult(VirusScanResult.Clean("NoOp"));
}
```

This is intentional for development and testing. **Replace it with a real scanner before deploying to production.**

---

## Implementing a real scanner

The following skeleton shows how to integrate ClamAV via its TCP clamd protocol (using a hypothetical `nClam` client):

```csharp
using ValiBlob.Core.Abstractions;

public sealed class ClamAvScanner : IVirusScanner
{
    private readonly ClamClient _clam;

    public ClamAvScanner(IOptions<ClamAvOptions> options)
    {
        _clam = new ClamClient(options.Value.Host, options.Value.Port);
    }

    public async Task<VirusScanResult> ScanAsync(
        Stream content,
        string? fileName,
        CancellationToken cancellationToken = default)
    {
        var scanResult = await _clam.SendAndScanFileAsync(content);

        return scanResult.Result switch
        {
            ClamScanResults.Clean => VirusScanResult.Clean("ClamAV"),
            ClamScanResults.VirusDetected =>
                VirusScanResult.Infected(scanResult.InfectedFiles?.FirstOrDefault()?.VirusName ?? "Unknown", "ClamAV"),
            _ => VirusScanResult.Infected("ScanError", "ClamAV")
        };
    }
}
```

Register it in DI:

```csharp
// Replace the default no-op scanner
builder.Services.AddSingleton<IVirusScanner, ClamAvScanner>();
builder.Services.Configure<ClamAvOptions>(builder.Configuration.GetSection("ClamAV"));
```

---

## Adding the middleware to the pipeline

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(p => p
        .WithContentTypeDetection(o => o.OverrideExisting = true) // 1. detect real type
        .UseValidation(v =>
        {
            v.AllowedContentTypes = new[] { "image/jpeg", "image/png", "application/pdf" };
        })                                                          // 2. enforce allowed types
        .WithVirusScan()                                            // 3. scan the content
    );
```

### Pipeline position recommendation

- Scan **after** `ContentTypeDetectionMiddleware` and `ValidationMiddleware` — this avoids scanning files that would be rejected anyway.
- Scan **before** the upload reaches the provider — a positive scan result cancels the upload and throws `StorageValidationException`.

---

## Behaviour on infection

When `IVirusScanner.ScanAsync` returns `IsClean = false`, the middleware:

1. Sets `context.IsCancelled = true`.
2. Sets `context.CancellationReason` to a human-readable message that includes the scanner name and threat name.
3. Throws `StorageValidationException` — the same exception type used by `ValidationMiddleware` — so your error handling is uniform.

```csharp
var result = await _storage.UploadAsync(request);

if (!result.IsSuccess)
{
    // result.ErrorMessage contains "File rejected by virus scanner 'ClamAV': Trojan.GenericKD"
    logger.LogWarning("Upload blocked: {Reason}", result.ErrorMessage);
    return Results.BadRequest(new { error = result.ErrorMessage });
}
```

---

## Stream position

After scanning, the middleware rewinds the stream to position 0 (if seekable) so the provider can read from the beginning. If your scanner implementation consumes the stream, ensure it also rewinds before returning — or pass a copy.
