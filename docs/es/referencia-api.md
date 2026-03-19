# Referencia de API

Documentación completa de todos los tipos públicos de ValiBlob.

---

## `IStorageProvider`

La interfaz central. Todos los proveedores la implementan.

**Namespace:** `ValiBlob.Core.Abstractions`

### Propiedades

| Propiedad | Tipo | Descripción |
|---|---|---|
| `ProviderName` | `string` | Nombre del proveedor (`"AWS"`, `"Azure"`, `"GCP"`, `"OCI"`, `"Supabase"`, `"InMemory"`) |

### Métodos

#### `UploadAsync`

```csharp
Task<StorageResult<UploadResult>> UploadAsync(
    UploadRequest request,
    IProgress<UploadProgress>? progress = null,
    CancellationToken cancellationToken = default)
```

Sube un archivo al storage. Pasa el request por el pipeline de middleware antes de enviarlo al proveedor.

- `request`: Datos del archivo a subir. Ver [UploadRequest](#uploadrequest).
- `progress`: Callback opcional para reportar progreso. Ver [UploadProgress](#uploadprogress).
- Retorna: `StorageResult<UploadResult>` con los datos del archivo subido.

#### `DownloadAsync`

```csharp
Task<StorageResult<Stream>> DownloadAsync(
    DownloadRequest request,
    CancellationToken cancellationToken = default)
```

Descarga un archivo del storage. El `Stream` retornado es responsabilidad del llamador (debe disponerse).

- `request`: Ruta y rango opcional. Ver [DownloadRequest](#downloadrequest).
- Retorna: `StorageResult<Stream>`. Si falla, `ErrorCode` puede ser `FileNotFound` o `AccessDenied`.

#### `DeleteAsync`

```csharp
Task<StorageResult> DeleteAsync(
    string path,
    CancellationToken cancellationToken = default)
```

Elimina un archivo. No falla si el archivo no existe (comportamiento idempotente).

- `path`: Ruta del archivo. Acepta `string` o `StoragePath` (conversión implícita).

#### `ExistsAsync`

```csharp
Task<StorageResult<bool>> ExistsAsync(
    string path,
    CancellationToken cancellationToken = default)
```

Verifica si un archivo existe. Retorna `StorageResult<bool>` con `Value = true` si existe.

#### `GetUrlAsync`

```csharp
Task<StorageResult<string>> GetUrlAsync(
    string path,
    CancellationToken cancellationToken = default)
```

Retorna la URL pública del archivo. Si `CdnBaseUrl` está configurado, retorna la URL del CDN. Para archivos privados, usá `IPresignedUrlProvider` en su lugar.

#### `CopyAsync`

```csharp
Task<StorageResult> CopyAsync(
    string sourcePath,
    string destinationPath,
    CancellationToken cancellationToken = default)
```

Copia un archivo dentro del mismo bucket/contenedor. Operación server-side — no descarga el contenido.

#### `MoveAsync`

```csharp
Task<StorageResult> MoveAsync(
    string sourcePath,
    string destinationPath,
    CancellationToken cancellationToken = default)
```

Mueve (copia + elimina) un archivo dentro del mismo bucket/contenedor.

#### `GetMetadataAsync`

```csharp
Task<StorageResult<FileMetadata>> GetMetadataAsync(
    string path,
    CancellationToken cancellationToken = default)
```

Obtiene la metadata de un archivo sin descargar su contenido. Ver [FileMetadata](#filemetadata).

#### `SetMetadataAsync`

```csharp
Task<StorageResult> SetMetadataAsync(
    string path,
    IDictionary<string, string> metadata,
    CancellationToken cancellationToken = default)
```

Actualiza la metadata de un archivo. El comportamiento varía por proveedor:
- **Azure/GCP**: actualización in-place eficiente
- **AWS S3**: requiere copia del objeto (transparente pero con costo de tráfico)
- **OCI/Supabase**: no soportado (`StorageErrorCode.NotSupported`)

#### `ListFilesAsync`

```csharp
Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesAsync(
    string? prefix = null,
    ListOptions? options = null,
    CancellationToken cancellationToken = default)
```

Lista archivos con una sola página de resultados. Para datasets grandes, usá `ListAllAsync`.

- `prefix`: Prefijo de ruta para filtrar. `null` lista todos los archivos.
- `options`: Ver [ListOptions](#listopciones).
- Retorna: Lista de [FileEntry](#fileentry).

#### `DeleteManyAsync`

```csharp
Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(
    IEnumerable<StoragePath> paths,
    CancellationToken cancellationToken = default)
```

Elimina múltiples archivos en batch. Ver [Operaciones batch](operaciones-batch.md).

#### `ListAllAsync`

```csharp
IAsyncEnumerable<FileEntry> ListAllAsync(
    string? prefix = null,
    CancellationToken cancellationToken = default)
```

Lista todos los archivos con paginación automática y streaming. No carga todos en memoria.

#### `DeleteFolderAsync`

```csharp
Task<StorageResult> DeleteFolderAsync(
    string prefix,
    CancellationToken cancellationToken = default)
```

Elimina todos los archivos cuya ruta empieza con `prefix`.

#### `ListFoldersAsync`

```csharp
Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(
    string? prefix = null,
    CancellationToken cancellationToken = default)
```

Lista los "directorios virtuales" bajo un prefijo dado.

#### `UploadFromUrlAsync`

```csharp
Task<StorageResult<UploadResult>> UploadFromUrlAsync(
    string sourceUrl,
    StoragePath destinationPath,
    string? bucketOverride = null,
    CancellationToken cancellationToken = default)
```

Descarga desde `sourceUrl` y sube directamente al storage. Ver [Operaciones batch](operaciones-batch.md).

---

## `IPresignedUrlProvider`

Interfaz implementada por los proveedores que soportan URLs prefirmadas/temporales.

**Namespace:** `ValiBlob.Core.Abstractions`

> **💡 Tip:** No todos los proveedores implementan esta interfaz. Verificá con un cast antes de usarla:
> ```csharp
> var presigned = factory.Create("AWS") as IPresignedUrlProvider;
> if (presigned is null) throw new NotSupportedException("Este proveedor no soporta URLs prefirmadas.");
> ```

#### `GetPresignedDownloadUrlAsync`

```csharp
Task<StorageResult<string>> GetPresignedDownloadUrlAsync(
    string path,
    TimeSpan expiration,
    CancellationToken cancellationToken = default)
```

Genera una URL temporal de descarga. La URL expira después del tiempo indicado.

#### `GetPresignedUploadUrlAsync`

```csharp
Task<StorageResult<string>> GetPresignedUploadUrlAsync(
    string path,
    TimeSpan expiration,
    CancellationToken cancellationToken = default)
```

Genera una URL temporal de upload. El cliente puede hacer `PUT` directamente a esta URL.

---

## `IStorageFactory`

Permite obtener instancias de proveedores registrados.

**Namespace:** `ValiBlob.Core.Abstractions`

#### `Create(string? providerName = null)`

```csharp
IStorageProvider Create(string? providerName = null)
```

Retorna el proveedor con el nombre dado, o el proveedor por defecto si `providerName` es `null`.

```csharp
var defaultProvider = factory.Create();       // proveedor por defecto
var awsProvider = factory.Create("AWS");      // proveedor específico
var azureProvider = factory.Create("Azure");
```

#### `Create<TProvider>()`

```csharp
IStorageProvider Create<TProvider>() where TProvider : IStorageProvider
```

Retorna un proveedor por tipo concreto.

#### `GetAll()`

```csharp
IEnumerable<IStorageProvider> GetAll()
```

Retorna todos los proveedores registrados.

---

## `IStorageEventHandler`

Interfaz para recibir notificaciones de eventos de storage.

**Namespace:** `ValiBlob.Core.Events`

Ver [Eventos](eventos.md) para documentación completa y ejemplos.

```csharp
Task OnUploadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default);
Task OnUploadFailedAsync(StorageEventContext context, CancellationToken cancellationToken = default);
Task OnDownloadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default);
Task OnDeleteCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default);
```

---

## `StoragePath`

Tipo de valor que representa una ruta de storage como segmentos tipados.

**Namespace:** `ValiBlob.Core.Models`

Ver [StoragePath](storage-path.md) para documentación completa.

### Miembros

| Miembro | Tipo | Descripción |
|---|---|---|
| `From(params string[] segments)` | `static StoragePath` | Crea desde uno o más segmentos |
| `Append(string segment)` | `StoragePath` | Nueva instancia con el segmento agregado |
| `operator /(StoragePath, string)` | `StoragePath` | Alias de `Append` |
| `FileName` | `string` | Último segmento (nombre del archivo) |
| `Extension` | `string?` | Extensión con punto (`".pdf"`), o `null` |
| `Parent` | `StoragePath?` | Ruta sin el último segmento, o `null` si hay un solo segmento |
| `Segments` | `IReadOnlyList<string>` | Todos los segmentos |
| `ToString()` | `string` | Segmentos unidos con `"/"` |
| `implicit operator string` | | Conversión a string |
| `implicit operator StoragePath(string)` | | Conversión desde string |
| `Equals`, `==`, `!=` | | Igualdad por valor, case-sensitive |

---

## `StorageResult<T>` y `StorageResult`

Tipo de retorno de todas las operaciones. Nunca lanza excepciones del proveedor — los errores se encapsulan en el resultado.

**Namespace:** `ValiBlob.Core.Models`

### `StorageResult<T>` — con valor

| Miembro | Tipo | Descripción |
|---|---|---|
| `IsSuccess` | `bool` | `true` si la operación fue exitosa |
| `Value` | `T?` | El valor retornado. Sólo válido cuando `IsSuccess = true` |
| `ErrorMessage` | `string?` | Mensaje de error legible. `null` si exitoso |
| `ErrorCode` | `StorageErrorCode` | Código de error estructurado. `None` si exitoso |
| `Exception` | `Exception?` | Excepción subyacente si aplica |
| `Success(T value)` | `static StorageResult<T>` | Factory de resultado exitoso |
| `Failure(string, StorageErrorCode, Exception?)` | `static StorageResult<T>` | Factory de resultado fallido |
| `Map<TResult>(Func<T, TResult>)` | `StorageResult<TResult>` | Transforma el valor si es exitoso |
| `implicit operator bool` | | Conversión a bool — retorna `IsSuccess` |

### `StorageResult` — sin valor (operaciones void)

Igual que `StorageResult<T>` pero sin `Value` ni `Map`. Usado por `DeleteAsync`, `CopyAsync`, `MoveAsync`, `SetMetadataAsync`, `DeleteFolderAsync`.

### Método `Map`

Permite transformar el valor interno sin necesidad de if/else:

```csharp
StorageResult<FileMetadata> metaResult = await _storage.GetMetadataAsync("archivo.pdf");

// Transformar a DTO sin chequeo manual de IsSuccess
StorageResult<ArchivoDto> dto = metaResult.Map(meta => new ArchivoDto
{
    Nombre = Path.GetFileName(meta.Path),
    TamanioMb = meta.SizeBytes / 1024.0 / 1024.0,
    UltimaModificacion = meta.LastModified
});

if (dto.IsSuccess)
    return Ok(dto.Value);
else
    return NotFound(dto.ErrorMessage);
```

---

## `UploadRequest`

Request para la operación `UploadAsync`.

**Namespace:** `ValiBlob.Core.Models`

| Propiedad | Tipo | Requerido | Descripción |
|---|---|---|---|
| `Path` | `StoragePath` | Sí | Ruta de destino en el storage |
| `Content` | `Stream` | Sí | Contenido del archivo |
| `ContentType` | `string?` | No | MIME type (ej: `"application/pdf"`) |
| `ContentLength` | `long?` | No | Tamaño en bytes. Requerido para progress reporting correcto |
| `Metadata` | `IDictionary<string, string>?` | No | Metadata personalizada del archivo |
| `Options` | `UploadOptions?` | No | Opciones de upload. Ver [UploadOptions](#uploadoptions) |
| `BucketOverride` | `string?` | No | Bucket/contenedor alternativo para esta operación |

### Métodos de `UploadRequest`

| Método | Descripción |
|---|---|
| `WithContent(Stream)` | Retorna una nueva instancia con el stream reemplazado (usado internamente por el pipeline) |
| `WithMetadata(IDictionary<string, string>)` | Retorna una nueva instancia con la metadata reemplazada |

### `UploadOptions`

| Propiedad | Tipo | Default | Descripción |
|---|---|---|---|
| `UseMultipart` | `bool` | `false` | Forzar subida multiparte |
| `ChunkSizeMb` | `int` | `8` | Tamaño de cada chunk en MB |
| `Overwrite` | `bool` | `true` | Si es `false`, falla si el archivo ya existe |
| `Encryption` | `StorageEncryptionMode` | `None` | Modo de cifrado del proveedor |

### `StorageEncryptionMode`

| Valor | Descripción |
|---|---|
| `None` | Sin cifrado |
| `ProviderManaged` | Cifrado gestionado por el proveedor (ej: SSE-S3, Azure Storage Service Encryption) |
| `ClientSide` | Cifrado del lado del cliente (vía `EncryptionMiddleware`) |

---

## `DownloadRequest`

Request para la operación `DownloadAsync`.

**Namespace:** `ValiBlob.Core.Models`

| Propiedad | Tipo | Requerido | Descripción |
|---|---|---|---|
| `Path` | `StoragePath` | Sí | Ruta del archivo a descargar |
| `Range` | `DownloadRange?` | No | Rango de bytes a descargar (partial content) |
| `BucketOverride` | `string?` | No | Bucket/contenedor alternativo para esta operación |

### `DownloadRange`

| Propiedad | Tipo | Descripción |
|---|---|---|
| `From` | `long` | Byte de inicio (inclusivo) |
| `To` | `long?` | Byte de fin (inclusivo). `null` = hasta el final del archivo |

---

## `FileMetadata`

Metadata de un archivo en el storage.

**Namespace:** `ValiBlob.Core.Models`

| Propiedad | Tipo | Descripción |
|---|---|---|
| `Path` | `string` | Ruta del archivo |
| `SizeBytes` | `long` | Tamaño en bytes |
| `ContentType` | `string?` | MIME type |
| `LastModified` | `DateTimeOffset?` | Fecha de última modificación |
| `CreatedAt` | `DateTimeOffset?` | Fecha de creación (no disponible en todos los proveedores) |
| `ETag` | `string?` | ETag del objeto (hash para comparar versiones) |
| `CustomMetadata` | `IDictionary<string, string>` | Metadata personalizada. Nunca es `null` (vacío si no hay) |

---

## `FileEntry`

Entrada en el resultado de `ListFilesAsync` y `ListAllAsync`.

**Namespace:** `ValiBlob.Core.Models`

| Propiedad | Tipo | Descripción |
|---|---|---|
| `Path` | `string` | Ruta completa del archivo |
| `SizeBytes` | `long` | Tamaño en bytes |
| `ContentType` | `string?` | MIME type |
| `LastModified` | `DateTimeOffset?` | Fecha de última modificación |
| `ETag` | `string?` | ETag del objeto |
| `IsDirectory` | `bool` | `true` si es un prefijo virtual (directorio) |

---

## `BatchDeleteResult`

Resultado de `DeleteManyAsync`.

**Namespace:** `ValiBlob.Core.Models`

| Propiedad | Tipo | Descripción |
|---|---|---|
| `TotalRequested` | `int` | Total de archivos en el request |
| `Deleted` | `int` | Cantidad eliminada exitosamente |
| `Failed` | `int` | Cantidad que falló |
| `Errors` | `IReadOnlyList<BatchDeleteError>` | Detalles de cada fallo |

### `BatchDeleteError`

| Propiedad | Tipo | Descripción |
|---|---|---|
| `Path` | `string` | Ruta del archivo que falló |
| `Reason` | `string` | Descripción del motivo del fallo |

---

## `UploadResult`

Resultado de una operación de upload exitosa.

**Namespace:** `ValiBlob.Core.Models`

| Propiedad | Tipo | Descripción |
|---|---|---|
| `Path` | `string` | Ruta final del archivo subido |
| `ETag` | `string?` | ETag asignado por el proveedor |
| `SizeBytes` | `long` | Tamaño del archivo subido |
| `Url` | `string?` | URL pública (si el proveedor la retorna en el upload) |
| `UploadedAt` | `DateTimeOffset` | Timestamp del upload (UTC). Default: `DateTimeOffset.UtcNow` |

---

## `UploadProgress`

Información de progreso reportada via `IProgress<UploadProgress>`.

**Namespace:** `ValiBlob.Core.Models`

| Miembro | Tipo | Descripción |
|---|---|---|
| `BytesTransferred` | `long` | Bytes transferidos hasta el momento |
| `TotalBytes` | `long?` | Total de bytes. `null` si se desconoce el tamaño total |
| `Percentage` | `double?` | Porcentaje completado. `null` si `TotalBytes` es `null` |
| `ToString()` | `string` | Representación legible: `"1.024 / 10.240 bytes (10,0%)"` |

---

## `ListOptions`

Opciones para `ListFilesAsync`.

**Namespace:** `ValiBlob.Core.Models`

| Propiedad | Tipo | Descripción |
|---|---|---|
| `MaxResults` | `int?` | Límite de resultados por página. `null` = sin límite |
| `ContinuationToken` | `string?` | Token para obtener la siguiente página |
| `IncludeDirectories` | `bool` | Si incluir prefijos virtuales (directorios) en el resultado |
| `Delimiter` | `string?` | Delimitador para agrupar archivos en "carpetas" |

---

## `StorageErrorCode`

Enum de códigos de error estructurados.

**Namespace:** `ValiBlob.Core.Models`

| Valor | Descripción |
|---|---|
| `None` | Sin error (operación exitosa) |
| `FileNotFound` | El archivo no existe en la ruta especificada |
| `AccessDenied` | Credenciales incorrectas o permisos insuficientes |
| `QuotaExceeded` | Cuota de almacenamiento excedida |
| `NetworkError` | Error de conectividad de red |
| `ValidationFailed` | El archivo no pasó las reglas del `ValidationMiddleware` |
| `ProviderError` | Error genérico del proveedor cloud (ver `ErrorMessage` para detalles) |
| `Timeout` | La operación superó el timeout configurado en `ResilienceOptions` |
| `NotSupported` | La operación no está soportada por este proveedor (ej: `SetMetadata` en Supabase) |
| `Conflict` | El archivo ya existe y `Overwrite = false` en las opciones de upload |
