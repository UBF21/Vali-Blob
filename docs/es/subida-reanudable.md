# Subida Reanudable (Resumable Uploads)

ValiBlob soporta subidas reanudables (chunked) para archivos grandes a través de la interfaz `IResumableUploadProvider`. Cada proveedor usa su mecanismo nativo internamente mientras expone una única API consistente.

---

## Soporte por proveedor

| Proveedor | Mecanismo | ¿Verdaderamente reanudable? |
|---|---|---|
| **AWS S3** | S3 Multipart Upload API | Sí — estado administrado por S3 |
| **Azure Blob** | Block Blobs (`StageBlockAsync`) | Sí — bloques staged persisten 7 días |
| **Supabase** | Protocolo nativo TUS 1.0.0 | Sí — semántica TUS completa |
| **GCP** | Buffer en archivo temporal + subida atómica | Parcial — chunks persisten en disco local; un reinicio de proceso pierde el buffer |
| **OCI** | OCI Multipart Upload API | Sí — similar a AWS |
| **InMemory** | Buffer en memoria ordenado por offset | Sí — para testing |

> **Nota GCP:** El SDK .NET de Google Cloud Storage no expone públicamente la URI interna de subida reanudable. ValiBlob almacena los chunks en un archivo temporal pre-asignado y sube el archivo completo atómicamente en `CompleteResumableUploadAsync`. Para subidas verdaderamente distribuidas en GCP, considerá implementar `IResumableSessionStore` con persistencia y un cliente REST de GCS personalizado.

---

## El ciclo de vida en 5 métodos

```
StartResumableUploadAsync     → crea la sesión, devuelve UploadId
      │
      ↓  (repetir por cada chunk)
UploadChunkAsync              → envía un chunk en un offset determinado
      │
      ↓  (opcional — para reanudar después de una interrupción)
GetUploadStatusAsync          → devuelve cuántos bytes recibió el proveedor
      │
      ↓
CompleteResumableUploadAsync  → finaliza el archivo en el proveedor
      │
      └──── AbortResumableUploadAsync  (llamar si querés cancelar)
```

---

## Configuración

### Configuración global (appsettings.json)

```json
{
  "ValiStorage:ResumableUpload": {
    "DefaultChunkSizeBytes": 8388608,
    "MinPartSizeBytes": 5242880,
    "SessionExpiration": "24:00:00",
    "EnableChecksumValidation": true,
    "MaxConcurrentChunks": 1
  }
}
```

| Propiedad | Valor por defecto | Descripción |
|---|---|---|
| `DefaultChunkSizeBytes` | 8 MB | Tamaño de chunk por defecto cuando no hay override por request |
| `MinPartSizeBytes` | 5 MB | Tamaño mínimo de parte. AWS S3 y OCI requieren ≥ 5 MB para partes no finales |
| `SessionExpiration` | 24 h | Cuánto tiempo permanece válida una sesión en el store |
| `EnableChecksumValidation` | `true` | Validar checksums MD5 por chunk cuando el proveedor lo soporta |
| `MaxConcurrentChunks` | 1 | Máximo de chunks paralelos (donde se soporte). `1` = secuencial estricto |

### Configuración por código

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithResumableUploads(o =>
    {
        o.DefaultChunkSizeBytes = 10 * 1024 * 1024; // 10 MB por chunk
        o.SessionExpiration = TimeSpan.FromHours(48);
        o.EnableChecksumValidation = true;
    });
```

### Override por request

```csharp
var request = new ResumableUploadRequest
{
    Path = StoragePath.From("videos", "clase.mp4"),
    TotalSize = tamañoArchivo,
    Options = new ResumableUploadRequestOptions
    {
        ChunkSizeBytes = 16 * 1024 * 1024,        // 16 MB para este archivo
        SessionExpiration = TimeSpan.FromHours(6)  // expiración más corta
    }
};
```

---

## Uso básico

### 1. Iniciar una sesión

```csharp
var provider = serviceProvider.GetRequiredKeyedService<IStorageProvider>("AWS")
    as IResumableUploadProvider;

var startResult = await provider.StartResumableUploadAsync(new ResumableUploadRequest
{
    Path = StoragePath.From("uploads", "videos", "conferencia.mp4"),
    ContentType = "video/mp4",
    TotalSize = infoArchivo.Length,
    Metadata = new Dictionary<string, string>
    {
        ["subido-por"] = userId,
        ["nombre-original"] = nombreArchivo
    }
});

if (!startResult.IsSuccess)
    return Problem(startResult.ErrorMessage);

var uploadId = startResult.Value!.UploadId;
// Guardá el uploadId para que el cliente pueda reanudar si hay una interrupción
```

### 2. Subir chunks

```csharp
const long tamañoChunk = 8 * 1024 * 1024; // 8 MB
using var streamArchivo = File.OpenRead(rutaLocal);

long offset = 0;
while (offset < infoArchivo.Length)
{
    var restante = infoArchivo.Length - offset;
    var tamañoActual = Math.Min(tamañoChunk, restante);

    var buffer = new byte[tamañoActual];
    var bytesLeídos = await streamArchivo.ReadAsync(buffer, 0, (int)tamañoActual);
    if (bytesLeídos == 0) break;

    var chunkResult = await provider.UploadChunkAsync(new ResumableChunkRequest
    {
        UploadId = uploadId,
        Data = new MemoryStream(buffer, 0, bytesLeídos),
        Offset = offset,
        Length = bytesLeídos
    });

    if (!chunkResult.IsSuccess)
        return Problem($"Chunk en offset {offset} falló: {chunkResult.ErrorMessage}");

    Console.WriteLine($"Progreso: {chunkResult.Value!.ProgressPercent:F1}%");
    offset += bytesLeídos;
}
```

### 3. Completar la subida

```csharp
var completeResult = await provider.CompleteResumableUploadAsync(uploadId);

if (!completeResult.IsSuccess)
    return Problem(completeResult.ErrorMessage);

Console.WriteLine($"Subida completa: {completeResult.Value!.Path} ({completeResult.Value.SizeBytes:N0} bytes)");
```

---

## Reanudar una subida interrumpida

Usá `GetUploadStatusAsync` para consultar cuántos bytes recibió el proveedor, y luego retomá desde ese offset:

```csharp
// Llamado cuando una subida previa fue interrumpida
public async Task ReanudarSubidaAsync(string uploadId, string rutaArchivoLocal)
{
    var statusResult = await provider.GetUploadStatusAsync(uploadId);
    if (!statusResult.IsSuccess)
    {
        // La sesión expiró o no existe — comenzar de nuevo
        await IniciarNuevaSubidaAsync(rutaArchivoLocal);
        return;
    }

    var status = statusResult.Value!;
    if (status.IsComplete)
    {
        Console.WriteLine("La subida ya estaba completada.");
        return;
    }

    Console.WriteLine($"Reanudando desde byte {status.BytesUploaded} de {status.TotalSize} ({status.ProgressPercent:F1}%)");

    // Reanudar subiendo desde el offset confirmado
    using var streamArchivo = File.OpenRead(rutaArchivoLocal);
    streamArchivo.Seek(status.BytesUploaded, SeekOrigin.Begin);

    await SubirChunksRestantesAsync(provider, uploadId, streamArchivo, status.BytesUploaded, status.TotalSize);

    await provider.CompleteResumableUploadAsync(uploadId);
}
```

---

## Cancelar una subida

```csharp
// Libera recursos en el proveedor (ID multipart, bloques staged, sesión TUS, archivo temporal)
var result = await provider.AbortResumableUploadAsync(uploadId);
if (result.IsSuccess)
    Console.WriteLine("Subida cancelada y recursos liberados.");
```

Siempre llamá `AbortResumableUploadAsync` cuando ya no tengas intención de completar una subida — esto libera recursos en la nube y almacenamiento facturado.

---

## Session store

Por defecto, ValiBlob usa un session store en memoria (`InMemoryResumableSessionStore`). Las sesiones se guardan en un `ConcurrentDictionary` y **se pierden al reiniciar el proceso**.

### Cuándo usar un store personalizado

- **Múltiples instancias de aplicación** (escalado horizontal, balanceo de carga)
- **Subidas de larga duración** que pueden sobrevivir a un solo proceso
- **Requisitos de auditoría** — log persistente de subidas

### Implementar un store personalizado

```csharp
public sealed class RedisResumableSessionStore : IResumableSessionStore
{
    private readonly IDatabase _db;

    public RedisResumableSessionStore(IConnectionMultiplexer redis)
        => _db = redis.GetDatabase();

    public async Task SaveAsync(ResumableUploadSession sesion, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(sesion);
        var expiracion = sesion.ExpiresAt.HasValue
            ? sesion.ExpiresAt.Value - DateTimeOffset.UtcNow
            : TimeSpan.FromHours(24);
        await _db.StringSetAsync($"valiblob:sesion:{sesion.UploadId}", json, expiracion);
    }

    public async Task<ResumableUploadSession?> GetAsync(string uploadId, CancellationToken ct = default)
    {
        var json = await _db.StringGetAsync($"valiblob:sesion:{uploadId}");
        return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<ResumableUploadSession>(json!);
    }

    public Task UpdateAsync(ResumableUploadSession sesion, CancellationToken ct = default)
        => SaveAsync(sesion, ct);

    public async Task DeleteAsync(string uploadId, CancellationToken ct = default)
        => await _db.KeyDeleteAsync($"valiblob:sesion:{uploadId}");
}
```

Registrarlo:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .UseResumableSessionStore<RedisResumableSessionStore>();
```

---

## Ejemplo con controlador ASP.NET Core

Una API HTTP completa para subidas en chunks:

```csharp
[ApiController]
[Route("api/subidas")]
public class SubidaReanudableController : ControllerBase
{
    private readonly IResumableUploadProvider _provider;

    public SubidaReanudableController(
        [FromKeyedServices("AWS")] IStorageProvider provider)
    {
        _provider = (IResumableUploadProvider)provider;
    }

    // POST /api/subidas/iniciar
    [HttpPost("iniciar")]
    public async Task<IActionResult> Iniciar([FromBody] IniciarSubidaDto dto)
    {
        var result = await _provider.StartResumableUploadAsync(new ResumableUploadRequest
        {
            Path = StoragePath.From("subidas", dto.NombreArchivo),
            ContentType = dto.ContentType,
            TotalSize = dto.TamañoTotal
        });

        if (!result.IsSuccess) return Problem(result.ErrorMessage);

        return Ok(new { uploadId = result.Value!.UploadId });
    }

    // PATCH /api/subidas/{uploadId}/chunk
    [HttpPatch("{uploadId}/chunk")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB max por chunk
    public async Task<IActionResult> SubirChunk(
        string uploadId,
        [FromHeader(Name = "Upload-Offset")] long offset)
    {
        var result = await _provider.UploadChunkAsync(new ResumableChunkRequest
        {
            UploadId = uploadId,
            Data = Request.Body,
            Offset = offset,
            Length = Request.ContentLength
        });

        if (!result.IsSuccess) return Problem(result.ErrorMessage);

        return Ok(new
        {
            bytesSubidos = result.Value!.BytesUploaded,
            porcentaje = result.Value.ProgressPercent,
            listoParaCompletar = result.Value.IsReadyToComplete
        });
    }

    // GET /api/subidas/{uploadId}/estado
    [HttpGet("{uploadId}/estado")]
    public async Task<IActionResult> ObtenerEstado(string uploadId)
    {
        var result = await _provider.GetUploadStatusAsync(uploadId);
        if (!result.IsSuccess) return NotFound(result.ErrorMessage);

        Response.Headers["Upload-Offset"] = result.Value!.BytesUploaded.ToString();
        Response.Headers["Upload-Length"] = result.Value.TotalSize.ToString();

        return Ok(result.Value);
    }

    // POST /api/subidas/{uploadId}/completar
    [HttpPost("{uploadId}/completar")]
    public async Task<IActionResult> Completar(string uploadId)
    {
        var result = await _provider.CompleteResumableUploadAsync(uploadId);
        if (!result.IsSuccess) return Problem(result.ErrorMessage);

        return Ok(new { ruta = result.Value!.Path, bytes = result.Value.SizeBytes });
    }

    // DELETE /api/subidas/{uploadId}
    [HttpDelete("{uploadId}")]
    public async Task<IActionResult> Cancelar(string uploadId)
    {
        var result = await _provider.AbortResumableUploadAsync(uploadId);
        if (!result.IsSuccess) return Problem(result.ErrorMessage);
        return NoContent();
    }
}

public record IniciarSubidaDto(string NombreArchivo, string ContentType, long TamañoTotal);
```

---

## Testing de subidas reanudables

`InMemoryStorageProvider` implementa `IResumableUploadProvider` con un buffer de chunks ordenado. No necesitás credenciales cloud.

```csharp
public class PruebasSubidaVideo
{
    private readonly InMemoryStorageProvider _provider;

    public PruebasSubidaVideo()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddValiBlob().UseInMemory();
        _provider = services.BuildServiceProvider()
            .GetRequiredService<InMemoryStorageProvider>();
    }

    [Fact]
    public async Task SubidaEnChunks_DebeRearmarArchivoCorrectamente()
    {
        var contenido = new byte[15 * 1024 * 1024]; // 15 MB
        new Random(42).NextBytes(contenido);

        // Iniciar
        var sesion = (await _provider.StartResumableUploadAsync(new ResumableUploadRequest
        {
            Path = StoragePath.From("videos", "test.mp4"),
            ContentType = "video/mp4",
            TotalSize = contenido.Length
        })).Value!;

        // Subir 3 × 5 MB chunks
        const int tamañoChunk = 5 * 1024 * 1024;
        for (int i = 0; i < 3; i++)
        {
            var chunk = contenido.Skip(i * tamañoChunk).Take(tamañoChunk).ToArray();
            await _provider.UploadChunkAsync(new ResumableChunkRequest
            {
                UploadId = sesion.UploadId,
                Data = new MemoryStream(chunk),
                Offset = i * tamañoChunk,
                Length = tamañoChunk
            });
        }

        // Completar
        var result = await _provider.CompleteResumableUploadAsync(sesion.UploadId);
        result.IsSuccess.Should().BeTrue();

        // Verificar
        var almacenado = _provider.GetRawBytes("videos/test.mp4");
        almacenado.Should().BeEquivalentTo(contenido);
    }
}
```

---

## Referencia de modelos

### `ResumableUploadRequest`

| Propiedad | Tipo | Descripción |
|---|---|---|
| `Path` | `StoragePath` | Ruta de destino (requerida) |
| `ContentType` | `string?` | Tipo MIME |
| `TotalSize` | `long` | Tamaño total del archivo en bytes |
| `Metadata` | `IDictionary<string, string>?` | Metadata personalizada |
| `BucketOverride` | `string?` | Override de bucket por request |
| `Options` | `ResumableUploadRequestOptions?` | Overrides de opciones por request |

### `ResumableChunkRequest`

| Propiedad | Tipo | Descripción |
|---|---|---|
| `UploadId` | `string` | ID de sesión devuelto por `StartResumableUploadAsync` |
| `Data` | `Stream` | Datos del chunk (leídos desde la posición actual) |
| `Offset` | `long` | Offset en bytes en el archivo completo (base cero) |
| `Length` | `long?` | Longitud del chunk. Si es null, lee hasta el fin del stream |

### `ChunkUploadResult`

| Propiedad | Tipo | Descripción |
|---|---|---|
| `UploadId` | `string` | ID de sesión |
| `BytesUploaded` | `long` | Total de bytes confirmados por el proveedor |
| `TotalSize` | `long` | Tamaño total declarado |
| `IsReadyToComplete` | `bool` | `true` cuando `BytesUploaded >= TotalSize` |
| `ProgressPercent` | `double` | Porcentaje de completitud (0–100) |

### `ResumableUploadStatus`

| Propiedad | Tipo | Descripción |
|---|---|---|
| `UploadId` | `string` | ID de sesión |
| `Path` | `string` | Ruta de destino |
| `TotalSize` | `long` | Tamaño total declarado |
| `BytesUploaded` | `long` | Bytes confirmados por el proveedor |
| `IsComplete` | `bool` | La subida fue completada |
| `IsAborted` | `bool` | La subida fue cancelada |
| `ExpiresAt` | `DateTimeOffset?` | Expiración de la sesión |
| `ProgressPercent` | `double` | Porcentaje de completitud (0–100) |

### `ResumableUploadSession`

Devuelta por `StartResumableUploadAsync`. Contiene el `UploadId` necesario para todas las llamadas siguientes. Guardalo del lado del cliente para soportar reanudación después de interrupciones.

---

## Códigos de error

| `StorageErrorCode` | Cuándo se devuelve |
|---|---|
| `FileNotFound` | `UploadId` no encontrado o sesión expirada |
| `ValidationFailed` | Operación intentada sobre una sesión cancelada |
| `ProviderError` | Error del proveedor (red, auth, cuota) |
| `NotSupported` | El proveedor no soporta subidas reanudables |
