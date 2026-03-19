# Proveedor Azure Blob Storage

Este documento cubre la configuración del proveedor `ValiBlob.Azure` para Microsoft Azure Blob Storage.

---

## Instalación

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.Azure
```

---

## Autenticación

Azure Blob Storage soporta dos formas de autenticación en ValiBlob.

### Opción 1: Connection String (más simple, recomendada para desarrollo)

La Connection String se obtiene en el portal de Azure → tu Storage Account → "Access keys".

```json
{
  "ValiBlob:Azure": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=miCuenta;AccountKey=miClave==;EndpointSuffix=core.windows.net",
    "Container": "mi-contenedor"
  }
}
```

```csharp
builder.Services
    .AddValiBlob()
    .UseAzure(opts =>
    {
        opts.ConnectionString = "DefaultEndpointsProtocol=https;...";
        opts.Container = "mi-contenedor";
    })
    .WithDefaultProvider("Azure");
```

### Opción 2: AccountName + AccountKey (control granular)

```json
{
  "ValiBlob:Azure": {
    "AccountName": "miCuentaStorage",
    "AccountKey": "base64EncodedKey==",
    "Container": "mi-contenedor"
  }
}
```

Con esta opción, ValiBlob construye la URL del servicio como:
`https://{AccountName}.blob.core.windows.net`

> **⚠️ Advertencia:** Las claves de Azure Storage tienen acceso completo a la cuenta. Para producción, considerá usar una SAS con permisos restringidos o Azure Managed Identity (que se puede configurar pasando una `DefaultAzureCredential` al registrar el `BlobServiceClient` manualmente).

> **💡 Tip:** Para desarrollo local podés usar el emulador Azurite:
> ```bash
> docker run -p 10000:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0
> ```
> Connection string del emulador: `UseDevelopmentStorage=true`

---

## Referencia completa de `AzureBlobOptions`

| Propiedad | Tipo | Default | Descripción |
|---|---|---|---|
| `ConnectionString` | `string?` | `null` | Connection string completa. Tiene prioridad sobre `AccountName` + `AccountKey` |
| `AccountName` | `string?` | `null` | Nombre de la Storage Account. Requerido si no se provee `ConnectionString` |
| `AccountKey` | `string?` | `null` | Clave de la Storage Account. Requerida si no se provee `ConnectionString` |
| `Container` | `string` | `""` | Nombre del contenedor Blob por defecto |
| `CdnBaseUrl` | `string?` | `null` | URL base del CDN. Si está presente, `GetUrlAsync` retorna la URL del CDN |
| `CreateContainerIfNotExists` | `bool` | `true` | Si es `true`, crea el contenedor automáticamente al inicializar si no existe |

---

## Creación automática de contenedores

Por defecto, ValiBlob.Azure crea el contenedor si no existe, evitando el error en el primer deploy. Podés desactivar este comportamiento:

```json
{
  "ValiBlob:Azure": {
    "Container": "mi-contenedor",
    "CreateContainerIfNotExists": false
  }
}
```

> **💡 Tip:** En producción con múltiples instancias de la aplicación, dejar `CreateContainerIfNotExists: true` es seguro — la operación es idempotente y Azure maneja la concurrencia correctamente.

---

## SAS Tokens (URLs prefirmadas)

`AzureBlobProvider` implementa `IPresignedUrlProvider`, permitiendo generar URLs temporales para que clientes accedan directamente al storage sin pasar por tu servidor.

```csharp
using ValiBlob.Core.Abstractions;

var factory = serviceProvider.GetRequiredService<IStorageFactory>();
var provider = factory.Create("Azure") as IPresignedUrlProvider;

if (provider is not null)
{
    // SAS de descarga — válida por 2 horas
    var downloadSas = await provider.GetPresignedDownloadUrlAsync(
        "documentos/contrato.pdf",
        TimeSpan.FromHours(2));

    if (downloadSas.IsSuccess)
        Console.WriteLine($"SAS de descarga: {downloadSas.Value}");

    // SAS de upload — válida por 30 minutos
    var uploadSas = await provider.GetPresignedUploadUrlAsync(
        "documentos/nuevo-archivo.pdf",
        TimeSpan.FromMinutes(30));

    if (uploadSas.IsSuccess)
        Console.WriteLine($"SAS de upload: {uploadSas.Value}");
}
```

Un caso de uso típico es devolver la SAS al cliente desde una API, para que el cliente suba el archivo directamente a Azure sin pasar el contenido por tu servidor:

```csharp
[HttpGet("upload-url")]
public async Task<IActionResult> ObtenerUrlDeUpload([FromQuery] string nombreArchivo)
{
    var provider = _factory.Create("Azure") as IPresignedUrlProvider;
    if (provider is null)
        return StatusCode(500, "El proveedor no soporta URLs prefirmadas.");

    var path = StoragePath.From("uploads", nombreArchivo);
    var resultado = await provider.GetPresignedUploadUrlAsync(path, TimeSpan.FromMinutes(15));

    if (!resultado.IsSuccess)
        return BadRequest(resultado.ErrorMessage);

    return Ok(new { url = resultado.Value, expiraEn = "15 minutos" });
}
```

---

## Configuración de CDN

Si tenés un Azure CDN o Front Door configurado frente a tu Blob Storage, podés configurar la URL base del CDN para que `GetUrlAsync` retorne URLs del CDN en lugar de URLs directas de Azure.

```json
{
  "ValiBlob:Azure": {
    "Container": "mi-contenedor",
    "ConnectionString": "DefaultEndpointsProtocol=https;...",
    "CdnBaseUrl": "https://cdn.midominio.com"
  }
}
```

Con esta configuración:
- Sin CDN: `GetUrlAsync("documentos/archivo.pdf")` retorna `https://miCuenta.blob.core.windows.net/mi-contenedor/documentos/archivo.pdf`
- Con CDN: retorna `https://cdn.midominio.com/documentos/archivo.pdf`

---

## BucketOverride

En Azure, el equivalente al bucket de S3 es el contenedor (container). `BucketOverride` permite especificar un contenedor diferente por request:

```csharp
// Subir en el contenedor del tenant
var result = await _storage.UploadAsync(new UploadRequest
{
    Path = StoragePath.From("reportes", "ventas.xlsx"),
    Content = stream,
    ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    BucketOverride = $"tenant-{tenantId}"
});

// Descargar del contenedor del tenant
var download = await _storage.DownloadAsync(new DownloadRequest
{
    Path = StoragePath.From("reportes", "ventas.xlsx"),
    BucketOverride = $"tenant-{tenantId}"
});
```

---

## Limitaciones

| Limitación | Detalle |
|---|---|
| Tamaño máximo de blob | 4.75 TB (block blob) |
| Nombres de contenedor | 3-63 caracteres, minúsculas, números y guiones |
| `SetMetadataAsync` | Azure permite actualizar metadata sin re-subir el archivo. Es una operación eficiente |
| Connection String | Contiene la clave de acceso completa; protegé bien este secreto |
| SAS Tokens | Requieren que la Storage Account tenga habilitado el acceso con clave compartida (puede desactivarse en configuraciones de seguridad estrictas) |
