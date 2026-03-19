# Operaciones Batch

ValiBlob incluye operaciones diseñadas para trabajar con múltiples archivos de manera eficiente: eliminación en batch, listado en streaming, operaciones de carpeta y upload remoto desde URL.

---

## `DeleteManyAsync`

Elimina múltiples archivos en una sola operación. Es mucho más eficiente que llamar `DeleteAsync` en un loop, ya que los proveedores que soportan batch delete (como AWS S3) lo ejecutan en una sola llamada a la API.

### Firma

```csharp
Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(
    IEnumerable<StoragePath> paths,
    CancellationToken cancellationToken = default)
```

### `BatchDeleteResult`

| Propiedad | Tipo | Descripción |
|---|---|---|
| `TotalRequested` | `int` | Total de archivos en el request |
| `Deleted` | `int` | Cantidad eliminada exitosamente |
| `Failed` | `int` | Cantidad que falló |
| `Errors` | `IReadOnlyList<BatchDeleteError>` | Lista de errores por archivo |

Cada `BatchDeleteError` tiene:
- `Path` (`string`): ruta del archivo que falló
- `Reason` (`string`): motivo del fallo

### Ejemplo de uso

```csharp
// Eliminar archivos temporales de una sesión de trabajo
var archivosTemporales = new[]
{
    StoragePath.From("temp", sessionId, "chunk-1.tmp"),
    StoragePath.From("temp", sessionId, "chunk-2.tmp"),
    StoragePath.From("temp", sessionId, "chunk-3.tmp"),
};

var result = await _storage.DeleteManyAsync(archivosTemporales);

if (result.IsSuccess)
{
    Console.WriteLine($"Eliminados: {result.Value!.Deleted} de {result.Value.TotalRequested}");

    if (result.Value.Failed > 0)
    {
        foreach (var error in result.Value.Errors)
            Console.WriteLine($"  Error en {error.Path}: {error.Reason}");
    }
}
```

### Manejo de errores parciales

`DeleteManyAsync` tiene un comportamiento de best-effort: si algunos archivos fallan pero otros no, el resultado general es `IsSuccess = true` y los fallos se reportan en `Errors`. Sólo retorna `IsSuccess = false` si hubo un error catastrófico que impidió procesar el batch.

```csharp
var result = await _storage.DeleteManyAsync(archivos);

if (!result.IsSuccess)
{
    // Error catastrófico — el batch no se procesó
    Console.WriteLine($"Error de batch [{result.ErrorCode}]: {result.ErrorMessage}");
    return;
}

// Procesar resultados parciales
Console.WriteLine($"Solicitados: {result.Value!.TotalRequested}");
Console.WriteLine($"Eliminados: {result.Value.Deleted}");
Console.WriteLine($"Fallaron: {result.Value.Failed}");

if (result.Value.Failed > 0)
{
    // Registrar los archivos que no se pudieron eliminar para retry posterior
    foreach (var error in result.Value.Errors)
    {
        _logger.LogWarning("No se pudo eliminar {Path}: {Reason}", error.Path, error.Reason);
    }
}
```

---

## `ListAllAsync`

Lista todos los archivos usando `IAsyncEnumerable<FileEntry>`. A diferencia de `ListFilesAsync`, maneja la paginación automáticamente y hace streaming de los resultados — no carga todos los archivos en memoria de una vez.

### Firma

```csharp
IAsyncEnumerable<FileEntry> ListAllAsync(
    string? prefix = null,
    CancellationToken cancellationToken = default)
```

### Cuándo usar `ListAllAsync` vs `ListFilesAsync`

| Escenario | Usar |
|---|---|
| Menos de 1.000 archivos | `ListFilesAsync` |
| Miles o millones de archivos | `ListAllAsync` |
| Necesitás paginación explícita | `ListFilesAsync` con `ContinuationToken` |
| Procesás cada archivo mientras llega | `ListAllAsync` |

### Ejemplo básico

```csharp
// Listar todos los archivos de una carpeta
await foreach (var entry in _storage.ListAllAsync("documentos/2024/"))
{
    Console.WriteLine($"{entry.Path} — {entry.SizeBytes:N0} bytes");
}
```

### Con cancelación

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

try
{
    await foreach (var entry in _storage.ListAllAsync("backups/", cts.Token))
    {
        await ProcesarArchivoAsync(entry);
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Listado cancelado por timeout.");
}
```

### Calcular tamaño total de una carpeta

```csharp
public async Task<long> CalcularTamanioTotalAsync(string prefijo)
{
    long totalBytes = 0;
    int totalArchivos = 0;

    await foreach (var entry in _storage.ListAllAsync(prefijo))
    {
        totalBytes += entry.SizeBytes;
        totalArchivos++;
    }

    Console.WriteLine($"Total: {totalArchivos} archivos, {totalBytes:N0} bytes ({totalBytes / 1024.0 / 1024:F1} MB)");
    return totalBytes;
}
```

### Filtrar y procesar en streaming

```csharp
// Encontrar todos los PDFs mayores a 10 MB
var archivosGrandes = new List<FileEntry>();

await foreach (var entry in _storage.ListAllAsync("documentos/"))
{
    if (entry.ContentType == "application/pdf" && entry.SizeBytes > 10 * 1024 * 1024)
    {
        archivosGrandes.Add(entry);
    }
}

Console.WriteLine($"PDFs mayores a 10 MB: {archivosGrandes.Count}");
```

---

## `DeleteFolderAsync`

Elimina todos los archivos que comienzan con un prefijo dado. Útil para limpiar carpetas completas.

### Firma

```csharp
Task<StorageResult> DeleteFolderAsync(
    string prefix,
    CancellationToken cancellationToken = default)
```

> **💡 Tip:** Los proveedores cloud no tienen "carpetas" reales — son prefijos. `DeleteFolderAsync` elimina todos los objetos cuya clave empieza con el prefijo dado.

### Ejemplo

```csharp
// Eliminar todos los archivos temporales de una sesión
var result = await _storage.DeleteFolderAsync($"temp/{sessionId}/");

if (result.IsSuccess)
    Console.WriteLine("Carpeta temporal eliminada.");
else
    Console.WriteLine($"Error: {result.ErrorMessage}");

// Eliminar todos los backups del año pasado
var añoPasado = DateTime.UtcNow.Year - 1;
await _storage.DeleteFolderAsync($"backups/{añoPasado}/");
```

> **⚠️ Advertencia:** `DeleteFolderAsync` es irreversible. Asegurate de que el prefijo sea suficientemente específico. Un prefijo vacío `""` o `"/"` podría eliminar todo el contenido del bucket.

---

## `ListFoldersAsync`

Lista los "directorios virtuales" (prefijos únicos) en una ruta. Útil para navegar la estructura de un bucket como si fuera un filesystem.

### Firma

```csharp
Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(
    string? prefix = null,
    CancellationToken cancellationToken = default)
```

### Ejemplo

```csharp
// Listar todos los tenants (carpetas raíz)
var result = await _storage.ListFoldersAsync("tenants/");

if (result.IsSuccess)
{
    foreach (var carpeta in result.Value!)
        Console.WriteLine($"Tenant: {carpeta}");
    // Salida: "acme-corp", "globex-inc", "initech"
}

// Listar años disponibles bajo "backups/"
var años = await _storage.ListFoldersAsync("backups/");
if (años.IsSuccess)
{
    foreach (var año in años.Value!)
        Console.WriteLine(año); // "2022", "2023", "2024"
}
```

---

## `UploadFromUrlAsync`

Descarga contenido desde una URL remota y lo sube directamente al storage, sin que el contenido pase por la memoria de tu servidor (o con un buffering mínimo, según la implementación del proveedor).

### Firma

```csharp
Task<StorageResult<UploadResult>> UploadFromUrlAsync(
    string sourceUrl,
    StoragePath destinationPath,
    string? bucketOverride = null,
    CancellationToken cancellationToken = default)
```

### Casos de uso

- Importar archivos desde URLs externas (APIs de terceros, S3 público, CDNs)
- Copiar recursos web al storage privado
- Migrar archivos entre proveedores sin pasar por tu servidor

### Ejemplo

```csharp
// Importar un logo desde una URL pública
var result = await _storage.UploadFromUrlAsync(
    sourceUrl: "https://ejemplo.com/recursos/logo.png",
    destinationPath: StoragePath.From("recursos", "logo.png"));

if (result.IsSuccess)
    Console.WriteLine($"Importado: {result.Value!.Path} ({result.Value.SizeBytes:N0} bytes)");

// Con BucketOverride para multi-tenant
var resultTenant = await _storage.UploadFromUrlAsync(
    sourceUrl: "https://proveedor.com/factura/12345.pdf",
    destinationPath: StoragePath.From("facturas", "12345.pdf"),
    bucketOverride: $"tenant-{tenantId}");
```

> **⚠️ Advertencia:** `UploadFromUrlAsync` no está soportado por el proveedor `InMemory` (retorna `StorageErrorCode.NotSupported`). Usalo con proveedores reales.

### Migración entre proveedores

```csharp
// Obtener URL del proveedor origen
var urlResult = await storageOrigen.GetUrlAsync("documentos/archivo.pdf");
if (!urlResult.IsSuccess) return;

// Subir en el proveedor destino usando la URL
var migrado = await storageDestino.UploadFromUrlAsync(
    sourceUrl: urlResult.Value!,
    destinationPath: StoragePath.From("documentos", "archivo.pdf"));
```

---

## Tips de rendimiento

### Procesar archivos en paralelo con grado de paralelismo controlado

```csharp
// Procesar hasta 10 archivos en paralelo
var semaphore = new SemaphoreSlim(10);
var tareas = new List<Task>();

await foreach (var entry in _storage.ListAllAsync("pendientes/"))
{
    await semaphore.WaitAsync();

    tareas.Add(Task.Run(async () =>
    {
        try
        {
            await ProcesarArchivoAsync(entry.Path);
        }
        finally
        {
            semaphore.Release();
        }
    }));
}

await Task.WhenAll(tareas);
```

### Batch delete eficiente con chunks

Cuando tenés una lista muy grande de archivos a eliminar, procesalos en chunks para no superar los límites del proveedor (AWS S3 acepta hasta 1.000 objetos por batch delete):

```csharp
public async Task EliminarEnChunksAsync(IEnumerable<StoragePath> paths, int chunkSize = 500)
{
    var lista = paths.ToList();
    var totalEliminados = 0;
    var totalFallidos = 0;

    for (var i = 0; i < lista.Count; i += chunkSize)
    {
        var chunk = lista.Skip(i).Take(chunkSize);
        var result = await _storage.DeleteManyAsync(chunk);

        if (result.IsSuccess)
        {
            totalEliminados += result.Value!.Deleted;
            totalFallidos += result.Value.Failed;
        }
        else
        {
            _logger.LogError("Error en chunk {Start}-{End}: {Error}",
                i, Math.Min(i + chunkSize, lista.Count), result.ErrorMessage);
        }
    }

    Console.WriteLine($"Eliminados: {totalEliminados}, Fallidos: {totalFallidos}");
}
```
