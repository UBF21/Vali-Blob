# Health Checks

ValiBlob incluye integración con el sistema de Health Checks de ASP.NET Core. El health check verifica que el proveedor de storage configurado es alcanzable y puede listar archivos, lo que valida credenciales, conectividad y permisos en una sola operación.

---

## Instalación

```bash
dotnet add package ValiBlob.HealthChecks
```

---

## Uso básico

Registrá el health check en el builder y mapeá el endpoint `/healthz`:

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.AWS.Extensions;
using ValiBlob.HealthChecks.Extensions;

builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithDefaultProvider("AWS");

builder.Services
    .AddHealthChecks()
    .AddValiBlob(); // verifica el proveedor por defecto

var app = builder.Build();

app.MapHealthChecks("/healthz");

app.Run();
```

Al hacer `GET /healthz`, obtenés:

```json
{
  "status": "Healthy",
  "results": {
    "valiblob": {
      "status": "Healthy",
      "duration": "00:00:00.1234567"
    }
  }
}
```

---

## Verificar un proveedor específico por nombre

Si registraste múltiples proveedores, podés agregar un health check por cada uno:

```csharp
builder.Services
    .AddHealthChecks()
    .AddValiBlob("AWS",    checkName: "storage-aws")
    .AddValiBlob("Azure",  checkName: "storage-azure")
    .AddValiBlob("GCP",    checkName: "storage-gcp");
```

---

## Referencia de `StorageHealthCheckOptions`

| Propiedad | Tipo | Default | Descripción |
|---|---|---|---|
| `ProbePrefix` | `string?` | `null` | Prefijo usado para la operación `ListFiles` de sondeo. Si es `null`, lista el root del bucket |
| `Timeout` | `TimeSpan` | `00:00:05` (5 segundos) | Timeout para la operación del health check |

```csharp
builder.Services
    .AddHealthChecks()
    .AddValiBlob(configure: opts =>
    {
        opts.ProbePrefix = "health-probe/"; // listar sólo este prefijo
        opts.Timeout = TimeSpan.FromSeconds(10);
    });
```

> **💡 Tip:** Usá `ProbePrefix` para apuntar a una carpeta dedicada al health check con archivos de prueba. Esto evita escanear todo el bucket en cada check de salud.

---

## Integrar con endpoint `/healthz` completo

```csharp
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text.Json;
using ValiBlob.HealthChecks.Extensions;

builder.Services
    .AddHealthChecks()
    .AddValiBlob("AWS", checkName: "storage-aws", tags: ["storage", "aws"])
    .AddValiBlob("Azure", checkName: "storage-azure", tags: ["storage", "azure"]);

var app = builder.Build();

// Endpoint simple — sólo el status
app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = _ => false // sólo verifica que la app está viva
});

// Endpoint detallado — con resultado JSON completo
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration
            })
        });
        await context.Response.WriteAsync(result);
    }
});
```

---

## Ejemplo completo con múltiples proveedores

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.AWS.Extensions;
using ValiBlob.Azure.Extensions;
using ValiBlob.HealthChecks.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Registrar proveedores
builder.Services
    .AddValiBlob()
    .UseAWS()
    .UseAzure()
    .WithDefaultProvider("AWS");

// Health checks con opciones por proveedor
builder.Services
    .AddHealthChecks()
    // AWS — con timeout corto y prefijo de sondeo
    .AddValiBlob(
        providerName: "AWS",
        checkName: "storage-aws",
        failureStatus: HealthStatus.Degraded, // no matar el pod si falla, sólo degradar
        tags: ["storage", "aws"],
        configure: opts =>
        {
            opts.ProbePrefix = "health/";
            opts.Timeout = TimeSpan.FromSeconds(5);
        })
    // Azure — falla crítica si no está disponible
    .AddValiBlob(
        providerName: "Azure",
        checkName: "storage-azure",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["storage", "azure"],
        configure: opts =>
        {
            opts.Timeout = TimeSpan.FromSeconds(8);
        });

var app = builder.Build();

app.MapHealthChecks("/healthz");
app.MapHealthChecks("/healthz/storage", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("storage")
});

app.Run();
```

Respuesta de ejemplo con múltiples proveedores:

```json
{
  "status": "Healthy",
  "results": {
    "storage-aws": {
      "status": "Healthy",
      "duration": "00:00:00.0892341"
    },
    "storage-azure": {
      "status": "Healthy",
      "duration": "00:00:00.1234567"
    }
  }
}
```

---

## Cómo funciona el health check

Internamente, `StorageProviderHealthCheck` llama a `ListFilesAsync` con el `ProbePrefix` configurado. Si la operación:

- **Retorna éxito**: el check reporta `Healthy`
- **Retorna falla**: el check reporta `Unhealthy` con el mensaje de error
- **Lanza excepción o supera el timeout**: el check reporta `Unhealthy`

Esto valida implícitamente:
- Conectividad de red al proveedor cloud
- Credenciales correctas
- Permisos de listado en el bucket/contenedor
- El bucket/contenedor existe

> **💡 Tip:** Para Kubernetes, configurá el health check de storage como readiness probe (`/healthz/ready`) en lugar de liveness probe. Si el storage no está disponible, no querés matar el pod — sólo querés que no reciba tráfico nuevo hasta que el storage se recupere.
