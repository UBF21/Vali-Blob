# Cuotas de almacenamiento

ValiBlob provee un sistema de cuotas que rastrea el almacenamiento utilizado por alcance y rechaza subidas que excederían el límite configurado. La aplicación de cuotas está integrada en el pipeline y funciona con cualquier proveedor de storage.

---

## La interfaz `IStorageQuotaService`

```csharp
public interface IStorageQuotaService
{
    Task<long> GetUsedBytesAsync(string scope, CancellationToken cancellationToken = default);
    Task RecordUploadAsync(string scope, long bytes, CancellationToken cancellationToken = default);
    Task RecordDeleteAsync(string scope, long bytes, CancellationToken cancellationToken = default);
    Task<long?> GetQuotaLimitAsync(string scope, CancellationToken cancellationToken = default);
}
```

Un **scope** (alcance) es cualquier string que identifica una unidad de facturación o aislamiento: un ID de tenant, un ID de usuario, un nombre de bucket, o cualquier clave personalizada que defina tu aplicación.

---

## `InMemoryStorageQuotaService` — la implementación por defecto

La implementación incluida almacena contadores en un `ConcurrentDictionary` thread-safe. Es adecuada para:

- Despliegues de instancia única
- Desarrollo y testing
- Aplicaciones donde el seguimiento aproximado (que se reinicia con el proceso) es aceptable

Los contadores de uso se **pierden cuando el proceso se reinicia**. Para seguimiento persistente, implementá `IStorageQuotaService` respaldado por Redis, una base de datos, o la API nativa de uso de tu proveedor cloud (ver nota de producción más abajo).

---

## Configuración

| Propiedad | Tipo | Valor por defecto | Descripción |
|---|---|---|---|
| `Enabled` | `bool` | `false` | Opt-in: debe establecerse explícitamente en `true` |
| `DefaultLimitBytes` | `long?` | `null` | Límite aplicado a todos los scopes sin una configuración específica. `null` = ilimitado |
| `Limits` | `Dictionary<string, long>` | `{}` | Límites por scope. La clave es el nombre del scope y el valor el límite en bytes |
| `ScopeResolver` | `Func<UploadRequest, string>?` | `null` | Función personalizada para derivar el scope de la solicitud. Por defecto: `BucketOverride ?? "default"` |

---

## Registro

### Cuota global básica

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithQuota(o =>
    {
        o.Enabled = true;
        o.DefaultLimitBytes = 5L * 1024 * 1024 * 1024; // 5 GB para todos
    });
```

### Cuotas por tenant

Usá `ScopeResolver` para derivar el scope de cada solicitud y configurá límites por tenant:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithQuota(o =>
    {
        o.Enabled = true;

        // Usar el BucketOverride como scope del tenant
        o.ScopeResolver = request => request.BucketOverride ?? "default";

        // Límites por tenant (en bytes)
        o.Limits["tenant-free"]       = 512L * 1024 * 1024;        // 512 MB
        o.Limits["tenant-pro"]        = 50L  * 1024 * 1024 * 1024; // 50 GB
        o.Limits["tenant-enterprise"] = long.MaxValue;               // ilimitado

        // Fallback para tenants no listados
        o.DefaultLimitBytes = 1L * 1024 * 1024 * 1024; // 1 GB
    });
```

Subida con scope por tenant:

```csharp
var request = new UploadRequest
{
    Path = StoragePath.From("reportes", "T4-2026.pdf"),
    Content = fileStream,
    ContentType = "application/pdf",
    BucketOverride = "tenant-pro" // scope = "tenant-pro"
};

var result = await _storage.UploadAsync(request);
```

### Resolver de scope personalizado

El scope no tiene que venir del `BucketOverride`. Podés resolverlo desde cualquier propiedad de la solicitud:

```csharp
o.ScopeResolver = request =>
{
    // Derivar scope del prefijo de la ruta
    var segmentos = request.Path.ToString().Split('/');
    return segmentos.Length > 1 ? segmentos[0] : "default";
};
```

---

## Comportamiento cuando se excede la cuota

Cuando una subida excedería la cuota, el pipeline se cancela y se lanza una excepción con un mensaje descriptivo:

```
StorageQuotaExceededException: Quota exceeded for scope 'tenant-free'.
  Used: 536870912 bytes, Limit: 536870912 bytes, Requested: 10485760 bytes.
```

Manejala junto a otros errores de validación:

```csharp
try
{
    var result = await _storage.UploadAsync(request);
}
catch (StorageQuotaExceededException ex)
{
    return Results.StatusCode(429, new { error = ex.Message });
}
```

---

## Consultar el uso actual

Inyectá `IStorageQuotaService` directamente para mostrar el uso a los usuarios:

```csharp
public class PanelAlmacenamientoService
{
    private readonly IStorageQuotaService _quota;

    public PanelAlmacenamientoService(IStorageQuotaService quota) => _quota = quota;

    public async Task<ResumenUso> ObtenerUsoAsync(string tenantId)
    {
        var usado = await _quota.GetUsedBytesAsync(tenantId);
        var limite = await _quota.GetQuotaLimitAsync(tenantId);

        return new ResumenUso
        {
            BytesUsados = usado,
            LimiteBytes = limite,
            PorcentajeUsado = limite.HasValue ? (double)usado / limite.Value * 100 : 0
        };
    }
}
```

---

## Nota de producción: reemplazá para despliegues multi-instancia

Los contadores del `InMemoryStorageQuotaService` existen solo en el proceso actual. En un despliegue multi-instancia (Kubernetes, App Service con múltiples réplicas, etc.), cada instancia mantiene su propio contador separado, por lo que el límite efectivo se multiplica por la cantidad de instancias.

Para producción, implementá `IStorageQuotaService` respaldado por un store compartido:

```csharp
// Implementación con Redis (esqueleto de ejemplo)
public sealed class RedisStorageQuotaService : IStorageQuotaService
{
    private readonly IDatabase _db;
    private readonly QuotaOptions _options;

    public RedisStorageQuotaService(IConnectionMultiplexer redis, QuotaOptions options)
    {
        _db = redis.GetDatabase();
        _options = options;
    }

    public async Task<long> GetUsedBytesAsync(string scope, CancellationToken ct = default)
        => (long)(await _db.StringGetAsync($"quota:used:{scope}"));

    public async Task RecordUploadAsync(string scope, long bytes, CancellationToken ct = default)
        => await _db.StringIncrementAsync($"quota:used:{scope}", bytes);

    public async Task RecordDeleteAsync(string scope, long bytes, CancellationToken ct = default)
        => await _db.StringDecrementAsync($"quota:used:{scope}", bytes);

    public Task<long?> GetQuotaLimitAsync(string scope, CancellationToken ct = default)
    {
        if (_options.Limits.TryGetValue(scope, out var limite))
            return Task.FromResult<long?>(limite);
        return Task.FromResult(_options.DefaultLimitBytes);
    }
}

// Registrarlo
builder.Services.AddSingleton<IStorageQuotaService, RedisStorageQuotaService>();
```
