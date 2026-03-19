# Escaneo de virus

ValiBlob provee un `VirusScanMiddleware` que integra el análisis antivirus en el pipeline de subida. La implementación del escáner está desacoplada detrás de la interfaz `IVirusScanner`, por lo que podés conectar cualquier motor: ClamAV, Windows Defender, una API comercial, etc.

---

## La interfaz `IVirusScanner`

```csharp
public interface IVirusScanner
{
    Task<VirusScanResult> ScanAsync(
        Stream content,
        string? fileName,
        CancellationToken cancellationToken = default);
}
```

`ScanAsync` recibe el stream de contenido crudo y el nombre del archivo (para contexto) y devuelve un `VirusScanResult`:

| Propiedad | Tipo | Descripción |
|---|---|---|
| `IsClean` | `bool` | `true` si no se encontró ninguna amenaza |
| `ThreatName` | `string?` | Nombre de la amenaza detectada (cuando `IsClean = false`) |
| `ScannerName` | `string?` | Identificador del escáner que produjo el resultado |

Métodos de fábrica de conveniencia:

```csharp
VirusScanResult.Clean("MiEscaner")
VirusScanResult.Infected("Trojan.GenericKD", "MiEscaner")
```

---

## `NoOpVirusScanner` — el valor por defecto

De forma predeterminada, ValiBlob registra el `NoOpVirusScanner` como implementación de `IVirusScanner`. Aprueba todos los archivos incondicionalmente:

```csharp
public sealed class NoOpVirusScanner : IVirusScanner
{
    public Task<VirusScanResult> ScanAsync(
        Stream content, string? fileName, CancellationToken cancellationToken = default)
        => Task.FromResult(VirusScanResult.Clean("NoOp"));
}
```

Esto es intencional para desarrollo y testing. **Reemplazalo por un escáner real antes de desplegar a producción.**

---

## Implementar un escáner real

El siguiente esqueleto muestra cómo integrar ClamAV a través de su protocolo TCP clamd (usando un cliente `nClam` hipotético):

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
                VirusScanResult.Infected(
                    scanResult.InfectedFiles?.FirstOrDefault()?.VirusName ?? "Unknown",
                    "ClamAV"),
            _ => VirusScanResult.Infected("ScanError", "ClamAV")
        };
    }
}
```

Registralo en DI:

```csharp
// Reemplazar el escáner no-op por defecto
builder.Services.AddSingleton<IVirusScanner, ClamAvScanner>();
builder.Services.Configure<ClamAvOptions>(builder.Configuration.GetSection("ClamAV"));
```

---

## Agregar el middleware al pipeline

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(p => p
        .WithContentTypeDetection(o => o.OverrideExisting = true) // 1. detectar tipo real
        .UseValidation(v =>
        {
            v.AllowedContentTypes = new[] { "image/jpeg", "image/png", "application/pdf" };
        })                                                          // 2. validar tipos permitidos
        .WithVirusScan()                                            // 3. escanear el contenido
    );
```

### Posición recomendada en el pipeline

- Escanear **después** del `ContentTypeDetectionMiddleware` y del `ValidationMiddleware` — así se evita escanear archivos que de todas formas serían rechazados.
- Escanear **antes** de que la subida llegue al proveedor — un resultado positivo cancela la subida y lanza `StorageValidationException`.

---

## Comportamiento ante una infección

Cuando `IVirusScanner.ScanAsync` devuelve `IsClean = false`, el middleware:

1. Establece `context.IsCancelled = true`.
2. Establece `context.CancellationReason` con un mensaje legible que incluye el nombre del escáner y el nombre de la amenaza.
3. Lanza `StorageValidationException` — el mismo tipo de excepción que usa el `ValidationMiddleware` — para que el manejo de errores sea uniforme.

```csharp
var result = await _storage.UploadAsync(request);

if (!result.IsSuccess)
{
    // result.ErrorMessage contiene "File rejected by virus scanner 'ClamAV': Trojan.GenericKD"
    logger.LogWarning("Subida bloqueada: {Reason}", result.ErrorMessage);
    return Results.BadRequest(new { error = result.ErrorMessage });
}
```

---

## Posición del stream

Después del escaneo, el middleware rebobina el stream a la posición 0 (si soporta seek) para que el proveedor pueda leer desde el principio. Si tu implementación del escáner consume el stream, asegurate de rebobinarlo antes de retornar — o pasá una copia.
