# Multi-tenant

En aplicaciones SaaS multi-tenant, cada cliente (tenant) generalmente necesita aislamiento de sus datos en el storage. ValiBlob ofrece `BucketOverride` como mecanismo central para esto, y esta sección muestra las estrategias más comunes.

---

## El problema multi-tenant

En una aplicación con múltiples clientes, hay dos enfoques principales para el storage:

**Opción A: Un bucket compartido con prefijo de tenant**
```
mi-bucket/
  tenants/
    acme-corp/
      facturas/
      reportes/
    globex-inc/
      facturas/
      reportes/
```

**Opción B: Un bucket por tenant**
```
acme-corp-bucket/
  facturas/
  reportes/

globex-inc-bucket/
  facturas/
  reportes/
```

ValiBlob soporta ambas estrategias. La opción A usa `StoragePath` con prefijo de tenant; la opción B usa `BucketOverride`.

---

## Usar BucketOverride por request

`BucketOverride` está disponible en `UploadRequest` y `DownloadRequest`. Permite especificar un bucket diferente para esa operación sin tocar la configuración global.

```csharp
// Subir en el bucket del tenant
var result = await _storage.UploadAsync(new UploadRequest
{
    Path = StoragePath.From("facturas", "FAC-001.pdf"),
    Content = stream,
    ContentType = "application/pdf",
    BucketOverride = $"tenant-{tenantId}" // bucket específico del tenant
});

// Descargar del bucket del tenant
var download = await _storage.DownloadAsync(new DownloadRequest
{
    Path = StoragePath.From("facturas", "FAC-001.pdf"),
    BucketOverride = $"tenant-{tenantId}"
});
```

---

## Resolver bucket dinámicamente con factory pattern

Para no tener que pasar `BucketOverride` manualmente en cada llamada, podés crear un servicio que encapsule esta lógica:

```csharp
public interface ITenantStorageService
{
    Task<StorageResult<UploadResult>> UploadAsync(UploadRequest request, string tenantId);
    Task<StorageResult<Stream>> DownloadAsync(DownloadRequest request, string tenantId);
    Task<StorageResult> DeleteAsync(string path, string tenantId);
}

public sealed class TenantStorageService : ITenantStorageService
{
    private readonly IStorageProvider _storage;
    private readonly ITenantResolver _tenantResolver;

    public TenantStorageService(
        IStorageFactory factory,
        ITenantResolver tenantResolver)
    {
        _storage = factory.Create();
        _tenantResolver = tenantResolver;
    }

    private string ResolveBucket(string tenantId) =>
        $"tenant-{tenantId.ToLowerInvariant()}-storage";

    public Task<StorageResult<UploadResult>> UploadAsync(UploadRequest request, string tenantId)
    {
        // Sobrescribir el BucketOverride con el bucket del tenant
        var requestConBucket = new UploadRequest
        {
            Path = request.Path,
            Content = request.Content,
            ContentType = request.ContentType,
            ContentLength = request.ContentLength,
            Metadata = request.Metadata,
            Options = request.Options,
            BucketOverride = ResolveBucket(tenantId)
        };

        return _storage.UploadAsync(requestConBucket);
    }

    public Task<StorageResult<Stream>> DownloadAsync(DownloadRequest request, string tenantId)
    {
        var requestConBucket = new DownloadRequest
        {
            Path = request.Path,
            Range = request.Range,
            BucketOverride = ResolveBucket(tenantId)
        };

        return _storage.DownloadAsync(requestConBucket);
    }

    public Task<StorageResult> DeleteAsync(string path, string tenantId) =>
        _storage.DeleteAsync(path); // DeleteAsync no tiene BucketOverride — usá el contexto del proveedor
}
```

---

## Estrategias de aislamiento por tenant

### Estrategia 1: Prefijo en la ruta (un bucket, separación lógica)

Ideal para pequeños SaaS o cuando crear N buckets no es práctico.

```csharp
public static class RutasTenant
{
    // Todos los archivos del tenant van bajo "tenants/{tenantId}/"
    public static StoragePath Para(string tenantId, string categoria, string archivo) =>
        StoragePath.From("tenants", tenantId, categoria, archivo);
}

// Uso
var ruta = RutasTenant.Para("acme-corp", "facturas", "FAC-001.pdf");
// "tenants/acme-corp/facturas/FAC-001.pdf"

var result = await _storage.UploadAsync(new UploadRequest
{
    Path = ruta,
    Content = stream,
    ContentType = "application/pdf"
    // Sin BucketOverride — todos en el mismo bucket
});
```

**Ventajas:** Simple, un bucket a gestionar.
**Desventajas:** Sin aislamiento fuerte — un error en la política IAM podría dar acceso cross-tenant.

### Estrategia 2: Bucket por tenant (aislamiento total)

Ideal para compliance, GDPR o cuando cada cliente exige aislamiento total.

```csharp
public sealed class BucketPerTenantStorageService
{
    private readonly IStorageProvider _storage;

    public BucketPerTenantStorageService(IStorageFactory factory)
    {
        _storage = factory.Create();
    }

    public async Task<StorageResult<UploadResult>> UploadAsync(
        string tenantId,
        StoragePath path,
        Stream content,
        string contentType)
    {
        return await _storage.UploadAsync(new UploadRequest
        {
            Path = path,
            Content = content,
            ContentType = contentType,
            BucketOverride = GetBucketName(tenantId)
        });
    }

    private static string GetBucketName(string tenantId) =>
        // Convención de nombre consistente
        $"mi-app-{tenantId.ToLowerInvariant().Replace(".", "-")}";
}
```

**Ventajas:** Aislamiento total, políticas IAM por tenant, facturación separada.
**Desventajas:** Gestión de N buckets, posibles límites del proveedor.

### Estrategia 3: Tenant desde el claim del usuario (automático)

Integración con el usuario autenticado de ASP.NET Core:

```csharp
public sealed class HttpContextTenantStorageService
{
    private readonly IStorageProvider _storage;
    private readonly IHttpContextAccessor _httpContext;

    public HttpContextTenantStorageService(
        IStorageFactory factory,
        IHttpContextAccessor httpContext)
    {
        _storage = factory.Create();
        _httpContext = httpContext;
    }

    private string ObtenerTenantId()
    {
        var tenantId = _httpContext.HttpContext?.User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantId))
            throw new UnauthorizedAccessException("No se encontró tenant_id en el token.");
        return tenantId;
    }

    public Task<StorageResult<UploadResult>> UploadAsync(UploadRequest request)
    {
        var tenantId = ObtenerTenantId();
        return _storage.UploadAsync(request with
        {
            BucketOverride = $"tenant-{tenantId}"
        });
    }
}
```

---

## Ejemplo completo en un controlador ASP.NET Core

```csharp
[ApiController]
[Route("api/documentos")]
[Authorize]
public class DocumentosController : ControllerBase
{
    private readonly IStorageProvider _storage;

    public DocumentosController(IStorageFactory factory)
    {
        _storage = factory.Create();
    }

    private string TenantId =>
        User.FindFirst("tenant_id")?.Value
        ?? throw new InvalidOperationException("Token sin tenant_id");

    private string BucketTenant => $"tenant-{TenantId.ToLowerInvariant()}";

    [HttpPost]
    public async Task<IActionResult> Subir(IFormFile archivo)
    {
        if (archivo.Length == 0)
            return BadRequest("El archivo está vacío.");

        var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        var path = StoragePath.From(
            "documentos",
            DateTime.UtcNow.Year.ToString(),
            DateTime.UtcNow.Month.ToString("D2"),
            $"{Guid.NewGuid()}{extension}");

        await using var stream = archivo.OpenReadStream();

        var result = await _storage.UploadAsync(new UploadRequest
        {
            Path = path,
            Content = stream,
            ContentType = archivo.ContentType,
            ContentLength = archivo.Length,
            BucketOverride = BucketTenant,
            Metadata = new Dictionary<string, string>
            {
                { "nombre-original", archivo.FileName },
                { "tenant", TenantId },
                { "usuario", User.Identity!.Name ?? "unknown" }
            }
        });

        if (!result.IsSuccess)
            return StatusCode(500, result.ErrorMessage);

        return Ok(new
        {
            path = result.Value!.Path,
            size = result.Value.SizeBytes,
            etag = result.Value.ETag
        });
    }

    [HttpGet("{**path}")]
    public async Task<IActionResult> Descargar(string path)
    {
        // Verificar que el archivo pertenece al tenant del usuario...
        var result = await _storage.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From(path),
            BucketOverride = BucketTenant
        });

        if (!result.IsSuccess)
        {
            if (result.ErrorCode == StorageErrorCode.FileNotFound)
                return NotFound();
            return StatusCode(500, result.ErrorMessage);
        }

        var metadata = await _storage.GetMetadataAsync(path);
        var contentType = metadata.IsSuccess
            ? metadata.Value!.ContentType ?? "application/octet-stream"
            : "application/octet-stream";

        return File(result.Value!, contentType);
    }

    [HttpDelete("{**path}")]
    public async Task<IActionResult> Eliminar(string path)
    {
        // Para Delete, el BucketOverride no aplica directamente en la firma de DeleteAsync.
        // Usá el servicio de tenant dedicado o el IStorageFactory para crear un provider
        // con el contexto del tenant ya configurado.
        var result = await _storage.DeleteAsync(path);

        if (!result.IsSuccess)
            return StatusCode(500, result.ErrorMessage);

        return NoContent();
    }
}
```

> **💡 Tip:** Para escenarios enterprise donde el `BucketOverride` en cada request resulta repetitivo, considerá crear un `IStorageProvider` decorador que automáticamente aplica el tenant del `IHttpContextAccessor` sin necesidad de pasarlo manualmente.
