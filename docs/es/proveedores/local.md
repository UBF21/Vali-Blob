# Proveedor de sistema de archivos local

El paquete `ValiBlob.Local` implementa `IStorageProvider` sobre el sistema de archivos local. Está diseñado para desarrollo, testing y entornos Docker Compose donde el almacenamiento cloud es innecesario o no está disponible.

---

## Descripción general

`ValiBlob.Local` almacena archivos como archivos físicos en disco, dentro de un directorio raíz configurable (`BasePath`). Soporta la superficie completa de `IStorageProvider` — subida, descarga, eliminación, copia, movimiento, listado, metadatos, operaciones de carpeta — además de subidas reanudables y stubs de URLs prefirmadas.

### Cuándo usarlo

| Escenario | Recomendado |
|---|---|
| Desarrollo local sin credenciales cloud | Sí |
| Entornos Docker Compose | Sí |
| Pipelines CI/CD sin acceso cloud | Sí |
| Tests de integración de código relacionado con storage | Sí |
| Cargas de trabajo en producción | No |

Para producción, usá AWS, Azure, GCP, OCI o Supabase. El proveedor local no ofrece redundancia, CDN ni escalabilidad horizontal.

---

## Instalación

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.Local
```

---

## Configuración

### `appsettings.json`

```json
{
  "ValiBlob": {
    "Local": {
      "BasePath": "/var/storage",
      "CreateIfNotExists": true,
      "PublicBaseUrl": "http://localhost:5000/files",
      "PreserveDirectoryStructure": true
    }
  }
}
```

### Registro en `Program.cs`

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Local.Extensions;

builder.Services
    .AddValiBlob()
    .UseLocal(o =>
    {
        o.BasePath = "/var/storage";
        o.PublicBaseUrl = "http://localhost:5000/files";
    });
```

### Referencia de `LocalStorageOptions`

| Propiedad | Tipo | Defecto | Descripción |
|---|---|---|---|
| `BasePath` | `string` | `""` (requerido) | Directorio raíz donde se almacenan todos los archivos |
| `CreateIfNotExists` | `bool` | `true` | Crea `BasePath` automáticamente si no existe |
| `PublicBaseUrl` | `string?` | `null` | URL base que se antepone a las rutas en `GetUrlAsync`. Si no se configura, devuelve una URI `file://` |
| `PreserveDirectoryStructure` | `bool` | `true` | Conserva la jerarquía de directorios original dentro de `BasePath` |

---

## Características

| Característica | Soporte |
|---|---|
| Subir / Descargar / Eliminar | Completo |
| Copiar / Mover | Completo |
| Existe / GetUrl | Completo |
| Listar archivos (prefijo, paginación) | Completo |
| Eliminación batch | Completo |
| Operaciones de carpeta (`DeleteFolderAsync`, `ListFoldersAsync`) | Completo |
| Metadatos mediante archivos sidecar | Completo |
| Subidas reanudables | Completo |
| Stubs de URLs prefirmadas | Sí (basado en tokens, solo para desarrollo local) |
| Descargas por rango | Sí (lecturas parciales de archivos) |

---

## Uso básico

El proveedor local se consume a través de la misma interfaz `IStorageProvider` que cualquier proveedor cloud:

```csharp
public class DocumentService
{
    private readonly IStorageProvider _storage;

    public DocumentService(IStorageProvider storage) => _storage = storage;

    public async Task<string> GuardarAsync(Stream contenido, string nombreArchivo)
    {
        var result = await _storage.UploadAsync(new UploadRequest
        {
            Path = StoragePath.From("documentos", nombreArchivo),
            Content = contenido,
            ContentType = "application/pdf"
        });

        if (!result.IsSuccess)
            throw new Exception($"Error al subir: {result.ErrorMessage}");

        return result.Value!.Path;
    }

    public async Task<Stream> CargarAsync(string nombreArchivo)
    {
        var result = await _storage.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From("documentos", nombreArchivo)
        });

        if (!result.IsSuccess)
            throw new Exception($"Error al descargar: {result.ErrorMessage}");

        return result.Value!;
    }
}
```

---

## Subidas reanudables

`ValiBlob.Local` implementa completamente `IResumableUploadProvider`. Los chunks se almacenan como archivos temporales y se ensamblan al completar la subida.

```csharp
// Inyectar IResumableUploadProvider desde DI
var session = await _resumable.InitiateResumableUploadAsync(new ResumableUploadRequest
{
    Path = StoragePath.From("videos", "intro.mp4"),
    ContentType = "video/mp4",
    TotalSize = fileStream.Length
});

// Subir cada chunk
var chunkSize = 5 * 1024 * 1024; // 5 MB
var buffer = new byte[chunkSize];
int bytesLeidos;
long offset = 0;

while ((bytesLeidos = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
{
    await _resumable.UploadChunkAsync(new ResumableChunkRequest
    {
        SessionId = session.Value!.SessionId,
        ChunkIndex = (int)(offset / chunkSize),
        Data = new MemoryStream(buffer, 0, bytesLeidos),
        Offset = offset
    });
    offset += bytesLeidos;
}

// Ensamblar el archivo final
var result = await _resumable.CompleteResumableUploadAsync(session.Value!.SessionId);
```

### Internals de la subida reanudable

Los chunks se almacenan en `{BasePath}/.resumable/{uploadId}/{offset}.chunk`. El estado de la sesión se persiste como un archivo JSON junto a los chunks. Al llamar a `CompleteResumableUploadAsync`, todos los chunks se ensamblan en orden en el archivo final en la ruta destino y el directorio de chunks temporales se elimina.

---

## Archivos sidecar de metadatos

Los metadatos del archivo (tipo de contenido, pares clave-valor personalizados, timestamps de subida) se almacenan en un archivo `.meta.json` adyacente al archivo de datos.

Para un archivo en `BasePath/documentos/informe.pdf`, los metadatos viven en `BasePath/documentos/informe.pdf.meta.json`.

El formato sidecar es un objeto JSON plano:

```json
{
  "content-type": "application/pdf",
  "x-subido-por": "usuario-123",
  "x-departamento": "finanzas"
}
```

Los archivos sidecar son gestionados de forma transparente por el proveedor. Los metadatos se acceden a través de los métodos estándar `GetMetadataAsync` y `SetMetadataAsync`.

---

## URLs prefirmadas (stubs para desarrollo local)

`ValiBlob.Local` produce URLs prefirmadas basadas en tokens, adecuadas para flujos de trabajo de desarrollo local pero que no están respaldadas por ningún control de acceso real. No deben usarse como mecanismo de seguridad.

```csharp
// Produce una URL como: http://localhost:5000/files/documentos/informe.pdf?token=<guid>&expires=<unix-ts>
var url = await _presigned.GetPresignedDownloadUrlAsync(new PresignedUrlRequest
{
    Path = StoragePath.From("documentos", "informe.pdf"),
    Expiration = TimeSpan.FromMinutes(15)
});
```

---

## Cambiar entre local y cloud

Un patrón común es usar `ValiBlob.Local` en desarrollo y un proveedor cloud en producción, sin cambios en el código de servicio:

```csharp
if (builder.Environment.IsDevelopment())
{
    builder.Services
        .AddValiBlob()
        .UseLocal(o => o.BasePath = "./local-storage");
}
else
{
    builder.Services
        .AddValiBlob()
        .UseAWS(builder.Configuration.GetSection("ValiBlob:AWS"));
}
```

Como todo el código de negocio depende únicamente de `IStorageProvider`, el cambio está completamente contenido en la raíz de composición.

---

## Ejemplo con Docker Compose

```yaml
services:
  api:
    build: .
    volumes:
      - storage-data:/var/storage
    environment:
      ValiBlob__Local__BasePath: /var/storage
      ValiBlob__Local__PublicBaseUrl: http://localhost:5000/files

volumes:
  storage-data:
```

---

## Limitaciones

- **Sin redundancia:** Los archivos se almacenan en un único disco. No hay replicación ni backup integrados.
- **Sin escalabilidad:** No funciona entre múltiples instancias de la aplicación que compartan un sistema de archivos, a menos que se monte un volumen de red compartido.
- **Los stubs de URLs prefirmadas no son seguros:** Las URLs generadas por este proveedor no aplican control de acceso por sí solas. No las expongas a clientes no confiables en producción.
- **No recomendado para producción:** Usá un proveedor cloud para cualquier carga de trabajo que requiera durabilidad, disponibilidad o distribución vía CDN.
