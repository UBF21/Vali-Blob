# Proveedor Supabase Storage

Este documento cubre la configuración del proveedor `ValiBlob.Supabase` para Supabase Storage.

---

## Instalación

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.Supabase
```

---

## Autenticación

Supabase Storage usa autenticación via URL del proyecto y API Key. Ambos valores se obtienen en el dashboard de Supabase → tu proyecto → Settings → API.

```json
{
  "ValiBlob:Supabase": {
    "Url": "https://xyzcompany.supabase.co",
    "ApiKey": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "Bucket": "mi-bucket"
  }
}
```

```csharp
builder.Services
    .AddValiBlob()
    .UseSupabase(opts =>
    {
        opts.Url = "https://xyzcompany.supabase.co";
        opts.ApiKey = configuration["Supabase:ApiKey"]!;
        opts.Bucket = "mi-bucket";
    })
    .WithDefaultProvider("Supabase");
```

> **⚠️ Advertencia:** Existen dos tipos de API Keys en Supabase:
> - **`anon` key**: clave pública con permisos limitados por las políticas RLS de Storage.
> - **`service_role` key**: clave privada con acceso completo, bypasea RLS. Nunca la expongas en el cliente.
>
> Para un backend server-to-server, usá la `service_role` key. Para uploads desde el cliente (browser/mobile), generá tokens temporales desde tu backend.

---

## Referencia completa de `SupabaseStorageOptions`

| Propiedad | Tipo | Default | Descripción |
|---|---|---|---|
| `Url` | `string` | `""` | URL del proyecto Supabase (ej: `https://xyzcompany.supabase.co`) |
| `ApiKey` | `string` | `""` | Service role key o anon key para autenticación |
| `Bucket` | `string` | `""` | Nombre del bucket por defecto |
| `CdnBaseUrl` | `string?` | `null` | URL base del CDN para archivos públicos |

---

## Buckets públicos vs privados

Supabase Storage soporta buckets públicos y privados.

### Bucket privado (default)

El acceso requiere autenticación. ValiBlob siempre incluye el `Authorization: Bearer {ApiKey}` header, por lo que todas las operaciones server-side funcionan correctamente.

```json
{
  "ValiBlob:Supabase": {
    "Url": "https://xyzcompany.supabase.co",
    "ApiKey": "service_role_key_here",
    "Bucket": "documentos-privados"
  }
}
```

### Bucket público

Los buckets públicos permiten acceso de lectura sin autenticación. Para `GetUrlAsync`, la URL retornada es la URL pública directa.

Si tenés un bucket público y querés configurar un CDN encima:

```json
{
  "ValiBlob:Supabase": {
    "Url": "https://xyzcompany.supabase.co",
    "ApiKey": "service_role_key_here",
    "Bucket": "imagenes-publicas",
    "CdnBaseUrl": "https://cdn.midominio.com"
  }
}
```

> **💡 Tip:** Los buckets se crean en el dashboard de Supabase → Storage → New bucket. No existe una opción para crearlos programáticamente en la API de Storage v1 que usa ValiBlob.

---

## URLs prefirmadas

`SupabaseStorageProvider` implementa `IPresignedUrlProvider`. Las URLs firmadas de Supabase permiten acceso temporal a archivos privados.

```csharp
var factory = serviceProvider.GetRequiredService<IStorageFactory>();
var provider = factory.Create("Supabase") as IPresignedUrlProvider;

if (provider is not null)
{
    // URL de descarga temporal — válida por 1 hora
    var downloadUrl = await provider.GetPresignedDownloadUrlAsync(
        "documentos/contrato.pdf",
        TimeSpan.FromHours(1));

    if (downloadUrl.IsSuccess)
        Console.WriteLine($"URL temporal: {downloadUrl.Value}");
}
```

Caso de uso típico — generar URL de descarga para el cliente desde tu API:

```csharp
[HttpGet("documentos/{id}/download-url")]
public async Task<IActionResult> ObtenerUrlDescarga(string id)
{
    // Verificar permisos del usuario actual...

    var provider = _factory.Create("Supabase") as IPresignedUrlProvider;
    var path = StoragePath.From("documentos", $"{id}.pdf");

    var resultado = await provider!.GetPresignedDownloadUrlAsync(path, TimeSpan.FromMinutes(30));

    if (!resultado.IsSuccess)
        return NotFound(resultado.ErrorMessage);

    return Ok(new { url = resultado.Value, expiraEn = "30 minutos" });
}
```

---

## Limitación de `SetMetadata`

> **⚠️ Advertencia:** La API de Supabase Storage v1 no expone un endpoint para actualizar metadata de objetos existentes. `SetMetadataAsync` en el proveedor Supabase retorna `StorageErrorCode.NotSupported`.

Si necesitás asociar metadata a archivos en Supabase, las alternativas son:
1. Guardar la metadata en una tabla de tu base de datos Supabase (PostgreSQL), relacionada por la ruta del archivo.
2. Incluir la metadata en el nombre de la carpeta como parte del path (por ejemplo, incluir el tenant o la fecha en la ruta).

```csharp
// Esto fallará en Supabase
var result = await _storage.SetMetadataAsync("archivo.pdf", new Dictionary<string, string>
{
    { "estado", "aprobado" }
});

if (!result.IsSuccess && result.ErrorCode == StorageErrorCode.NotSupported)
{
    // Guardar la metadata en la base de datos
    await _dbContext.ArchivoMetadata.AddAsync(new ArchivoMetadata
    {
        Path = "archivo.pdf",
        Estado = "aprobado",
        ActualizadoEn = DateTimeOffset.UtcNow
    });
    await _dbContext.SaveChangesAsync();
}
```

---

## BucketOverride

```csharp
// Usar un bucket diferente por operación
var result = await _storage.UploadAsync(new UploadRequest
{
    Path = StoragePath.From("avatares", "usuario-123.jpg"),
    Content = imageStream,
    ContentType = "image/jpeg",
    BucketOverride = "imagenes-publicas" // diferente del bucket configurado por defecto
});
```

---

## Ejemplo completo con múltiples operaciones

```csharp
public class ServicioDocumentos
{
    private readonly IStorageProvider _storage;
    private readonly IPresignedUrlProvider _presignedProvider;

    public ServicioDocumentos(IStorageFactory factory)
    {
        _storage = factory.Create("Supabase");
        _presignedProvider = (IPresignedUrlProvider)_storage;
    }

    public async Task<string> SubirYObtenerUrl(Stream contenido, string nombre)
    {
        var path = StoragePath.From("documentos", nombre);

        var upload = await _storage.UploadAsync(new UploadRequest
        {
            Path = path,
            Content = contenido,
            ContentType = "application/pdf"
        });

        if (!upload.IsSuccess)
            throw new InvalidOperationException($"Error al subir: {upload.ErrorMessage}");

        // Generar URL temporal para el cliente
        var urlResult = await _presignedProvider.GetPresignedDownloadUrlAsync(
            path, TimeSpan.FromHours(24));

        return urlResult.IsSuccess
            ? urlResult.Value!
            : throw new InvalidOperationException($"Error al generar URL: {urlResult.ErrorMessage}");
    }
}
```

---

## Limitaciones

| Limitación | Detalle |
|---|---|
| `SetMetadataAsync` | No soportado — retorna `StorageErrorCode.NotSupported` |
| `UploadFromUrlAsync` | No soportado por la API de Storage v1 |
| Tamaño máximo de archivo | Depende del plan de Supabase (Free: 50 MB, Pro: configurable hasta 5 GB) |
| Creación de buckets | Los buckets deben crearse manualmente en el dashboard |
| Políticas RLS | Con `service_role` key se bypasea RLS. Si usás `anon` key, configurá las políticas de Storage en el dashboard |
| Regiones | La región del proyecto Supabase determina la región del storage. No se puede cambiar por operación |
