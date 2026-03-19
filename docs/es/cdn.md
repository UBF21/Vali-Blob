# Integración con CDN

ValiBlob provee una abstracción `ICdnProvider` que mapea rutas de storage a URLs de CDN. Esto desacopla tu aplicación de un proveedor CDN específico y facilita agregar hooks de invalidación de caché.

---

## La interfaz `ICdnProvider`

```csharp
public interface ICdnProvider
{
    string GetCdnUrl(string storagePath);
    Task InvalidateCacheAsync(string storagePath, CancellationToken cancellationToken = default);
}
```

| Método | Descripción |
|---|---|
| `GetCdnUrl` | Devuelve la URL pública de CDN para una ruta de storage |
| `InvalidateCacheAsync` | Envía una solicitud de invalidación de caché al CDN para la ruta dada |

---

## `PrefixCdnProvider` — el proveedor incluido

`PrefixCdnProvider` mapea una ruta de storage a una URL de CDN combinando una URL base configurable con la ruta:

```
storagePath = "avatares/usuario-42/perfil.jpg"
BaseUrl     = "https://cdn.ejemplo.com"

resultado   = "https://cdn.ejemplo.com/avatares/usuario-42/perfil.jpg"
```

### Registro

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithCdn(o =>
    {
        o.BaseUrl = "https://cdn.ejemplo.com";
    });
```

### `CdnOptions`

| Propiedad | Tipo | Valor por defecto | Descripción |
|---|---|---|---|
| `BaseUrl` | `string` | `""` | URL base del CDN, por ejemplo `https://cdn.ejemplo.com` |
| `StripPrefix` | `string?` | `null` | Prefijo de ruta opcional a eliminar de las rutas de storage antes de agregarlas a la URL base |

### Usando `StripPrefix`

Algunos layouts de storage usan un prefijo de bucket o entorno que no debería aparecer en las URLs del CDN:

```csharp
.WithCdn(o =>
{
    o.BaseUrl = "https://cdn.ejemplo.com";
    o.StripPrefix = "produccion/"; // "produccion/imagenes/gato.jpg" → CDN: "imagenes/gato.jpg"
});
```

---

## Usar `ICdnProvider` en tu código

Inyectá `ICdnProvider` donde necesites construir URLs públicas:

```csharp
public class ServicioUrlArchivos
{
    private readonly ICdnProvider _cdn;

    public ServicioUrlArchivos(ICdnProvider cdn) => _cdn = cdn;

    public string ObtenerUrlPublica(string rutaStorage)
    {
        return _cdn.GetCdnUrl(rutaStorage);
        // "https://cdn.ejemplo.com/avatares/usuario-42/perfil.jpg"
    }

    public async Task PurgarAsync(string rutaStorage, CancellationToken ct)
    {
        await _cdn.InvalidateCacheAsync(rutaStorage, ct);
    }
}
```

---

## Implementar un proveedor personalizado

`PrefixCdnProvider` no realiza llamadas reales a API para invalidar caché — su `InvalidateCacheAsync` es un no-op. Para integración real con CDN, implementá `ICdnProvider` y llamá a la API del proveedor:

### Esqueleto para CloudFront

```csharp
using Amazon.CloudFront;
using ValiBlob.Core.Abstractions;

public sealed class CloudFrontCdnProvider : ICdnProvider
{
    private readonly IAmazonCloudFront _cloudFront;
    private readonly string _distributionId;
    private readonly string _baseUrl;

    public CloudFrontCdnProvider(
        IAmazonCloudFront cloudFront,
        IOptions<CloudFrontOptions> options)
    {
        _cloudFront = cloudFront;
        _distributionId = options.Value.DistributionId;
        _baseUrl = options.Value.BaseUrl.TrimEnd('/');
    }

    public string GetCdnUrl(string storagePath)
        => $"{_baseUrl}/{storagePath.TrimStart('/')}";

    public async Task InvalidateCacheAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        await _cloudFront.CreateInvalidationAsync(new CreateInvalidationRequest
        {
            DistributionId = _distributionId,
            InvalidationBatch = new InvalidationBatch
            {
                Paths = new Paths { Items = new List<string> { $"/{storagePath}" }, Quantity = 1 },
                CallerReference = Guid.NewGuid().ToString()
            }
        }, cancellationToken);
    }
}
```

### Esqueleto para Cloudflare

```csharp
public sealed class CloudflareCdnProvider : ICdnProvider
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _zoneId;

    public string GetCdnUrl(string storagePath)
        => $"{_baseUrl.TrimEnd('/')}/{storagePath.TrimStart('/')}";

    public async Task InvalidateCacheAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.cloudflare.com/client/v4/zones/{_zoneId}/purge_cache";
        var body = JsonSerializer.Serialize(new { files = new[] { GetCdnUrl(storagePath) } });
        await _http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), cancellationToken);
    }
}
```

### Registro de un proveedor personalizado

```csharp
// Reemplazar el PrefixCdnProvider por defecto
builder.Services.AddSingleton<ICdnProvider, CloudFrontCdnProvider>();
```

---

## Hook de invalidación de caché

Llamá a `InvalidateCacheAsync` después de eliminar o reemplazar un archivo para asegurarte de que el contenido desactualizado se purgue del CDN:

```csharp
public async Task ReemplazarArchivoAsync(string ruta, Stream nuevoContenido, CancellationToken ct)
{
    // Subir la nueva versión
    await _storage.UploadAsync(new UploadRequest
    {
        Path = StoragePath.From(ruta),
        Content = nuevoContenido,
        ConflictResolution = ConflictResolution.Overwrite
    }, cancellationToken: ct);

    // Purgar la caché del CDN para que se sirva la nueva versión inmediatamente
    await _cdn.InvalidateCacheAsync(ruta, ct);
}
```
