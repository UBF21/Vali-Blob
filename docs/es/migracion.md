# Migración de storage

`IStorageMigrator` copia archivos de un proveedor ValiBlob a otro. Soporta filtrado por prefijo, modo de prueba (dry run), eliminación del origen, omisión de existentes, y reporte de progreso en tiempo real.

---

## Casos de uso

- Migrar de AWS S3 a Azure Blob Storage (o cualquier combinación de proveedores)
- Mover archivos entre buckets dentro del mismo proveedor
- Archivar un subconjunto de archivos en almacenamiento más económico
- Validar la viabilidad de una migración con un dry run antes de comprometerse

---

## `MigrationOptions`

| Propiedad | Tipo | Valor por defecto | Descripción |
|---|---|---|---|
| `Prefix` | `string?` | `null` | Migrar solo archivos cuya ruta comience con este prefijo. `null` = todos los archivos |
| `DryRun` | `bool` | `false` | Si es `true`, simula la migración y reporta resultados sin transferir datos |
| `DeleteSourceAfterCopy` | `bool` | `false` | Si es `true`, elimina cada archivo del origen después de una copia exitosa |
| `SkipExisting` | `bool` | `true` | Omitir archivos que ya existen en el destino |
| `MaxFiles` | `int?` | `null` | Cantidad máxima de archivos a procesar. `null` = todos |

---

## `MigrationResult`

| Propiedad | Tipo | Descripción |
|---|---|---|
| `TotalFiles` | `int` | Archivos encontrados en el origen (luego del filtrado por prefijo y `MaxFiles`) |
| `Migrated` | `int` | Archivos copiados exitosamente (o contados, durante dry run) |
| `Skipped` | `int` | Archivos omitidos porque ya existían en el destino |
| `Failed` | `int` | Archivos que encontraron un error |
| `Errors` | `IReadOnlyList<MigrationError>` | Detalles de error por archivo (`Path` + `Reason`) |
| `Duration` | `TimeSpan` | Tiempo total transcurrido |
| `TotalBytesTransferred` | `long` | Bytes efectivamente transferidos (0 durante dry run) |

---

## Ejemplo básico: migrar de AWS a Azure

Ambos proveedores deben estar registrados en el contenedor de DI:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()    // registrado como "AWS"
    .UseAzure(); // registrado como "Azure"
```

Inyectá `IStorageMigrator` y llamá a `MigrateAsync`:

```csharp
public class ServicioMigracion
{
    private readonly IStorageMigrator _migrador;

    public ServicioMigracion(IStorageMigrator migrador) => _migrador = migrador;

    public async Task MigrarAzureAsync(CancellationToken ct)
    {
        var result = await _migrador.MigrateAsync(
            sourceProviderName: "AWS",
            destinationProviderName: "Azure",
            options: new MigrationOptions
            {
                SkipExisting = true,
                DeleteSourceAfterCopy = false // mantener origen hasta verificar migración
            },
            cancellationToken: ct);

        Console.WriteLine($"Migración completada en {result.Duration.TotalSeconds:F1}s");
        Console.WriteLine($"  Migrados : {result.Migrated}");
        Console.WriteLine($"  Omitidos : {result.Skipped}");
        Console.WriteLine($"  Fallidos : {result.Failed}");
        Console.WriteLine($"  Bytes    : {result.TotalBytesTransferred:N0}");

        foreach (var error in result.Errors)
            Console.WriteLine($"  ERROR {error.Path}: {error.Reason}");
    }
}
```

---

## Reporte de progreso

Pasá un `IProgress<MigrationProgress>` para recibir actualizaciones por archivo:

```csharp
var progreso = new Progress<MigrationProgress>(p =>
{
    Console.WriteLine($"[{p.Percentage:F1}%] {p.ProcessedFiles}/{p.TotalFiles} — {p.CurrentFile}");
});

var result = await _migrador.MigrateAsync(
    sourceProviderName: "AWS",
    destinationProviderName: "Azure",
    progress: progreso,
    cancellationToken: ct);
```

Propiedades de `MigrationProgress`:

| Propiedad | Tipo | Descripción |
|---|---|---|
| `TotalFiles` | `int` | Total de archivos a procesar |
| `ProcessedFiles` | `int` | Archivos procesados hasta ahora |
| `CurrentFile` | `string` | Ruta del archivo que se está transfiriendo |
| `Percentage` | `double` | `ProcessedFiles / TotalFiles * 100` |

---

## Flujo de trabajo con dry run

Ejecutá primero un dry run para validar que la migración sería exitosa antes de tocar datos:

```csharp
// Paso 1: dry run
var dryResult = await _migrador.MigrateAsync(
    sourceProviderName: "AWS",
    destinationProviderName: "Azure",
    options: new MigrationOptions { DryRun = true, SkipExisting = true });

Console.WriteLine($"Se migrarían {dryResult.Migrated} archivos ({dryResult.Skipped} ya existen).");

if (dryResult.Failed > 0)
{
    Console.WriteLine("El dry run detectó errores — corregí antes de continuar.");
    return;
}

// Paso 2: migración real
var result = await _migrador.MigrateAsync(
    sourceProviderName: "AWS",
    destinationProviderName: "Azure",
    options: new MigrationOptions
    {
        DryRun = false,
        SkipExisting = true,
        DeleteSourceAfterCopy = true // semántica de mover
    });
```

Durante un dry run no se producen descargas ni subidas. El migrador enumera el origen, verifica existencia en el destino y cuenta lo que se transferiría.

---

## Filtrado por prefijo

Migrá solo un subconjunto de archivos especificando un prefijo de ruta:

```csharp
var result = await _migrador.MigrateAsync(
    sourceProviderName: "AWS",
    destinationProviderName: "Azure",
    options: new MigrationOptions
    {
        Prefix = "facturas/2025/",  // solo migrar facturas de 2025
        SkipExisting = true
    });
```

---

## Cancelación

Pasá un `CancellationToken` para detener la migración de forma ordenada. El migrador verifica el token entre archivos:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromHours(2));

var result = await _migrador.MigrateAsync(
    "AWS", "Azure",
    cancellationToken: cts.Token);
```

Los archivos ya transferidos antes de la cancelación no se revierten.

---

## Manejo de errores

Los errores en archivos individuales se registran en `MigrationResult.Errors` y no detienen la migración general. Después de la migración, revisá `result.Failed` y `result.Errors` para identificar y reintentar los archivos fallidos:

```csharp
if (result.Failed > 0)
{
    foreach (var err in result.Errors)
        logger.LogError("Migración fallida para {Path}: {Reason}", err.Path, err.Reason);
}
```
