# Telemetría

ValiBlob incluye telemetría OpenTelemetry lista para usar. Todas las operaciones de storage generan métricas y actividades (trazas distribuidas) automáticamente, sin necesidad de instrumentar tu código.

---

## Integración con OpenTelemetry

ValiBlob usa las APIs estándar de .NET (`System.Diagnostics.ActivitySource` y `System.Diagnostics.Metrics`) que son el fundamento de OpenTelemetry en .NET. La telemetría se activa automáticamente cuando `EnableTelemetry = true` (valor por defecto).

```json
{
  "ValiBlob": {
    "EnableTelemetry": true
  }
}
```

---

## Métricas disponibles

Todas las métricas se publican bajo el meter `"ValiBlob"` (versión `"1.0.0"`).

| Nombre de métrica | Tipo | Unidad | Descripción |
|---|---|---|---|
| `valiblob.uploads.total` | Counter | `uploads` | Total de operaciones de upload realizadas |
| `valiblob.downloads.total` | Counter | `downloads` | Total de operaciones de download realizadas |
| `valiblob.deletes.total` | Counter | `deletes` | Total de operaciones de delete realizadas |
| `valiblob.uploads.bytes` | Counter | `bytes` | Total de bytes subidos |
| `valiblob.upload.duration_ms` | Histogram | `ms` | Duración de uploads en milisegundos |
| `valiblob.download.duration_ms` | Histogram | `ms` | Duración de downloads en milisegundos |

Todas las métricas incluyen el tag `provider` con el nombre del proveedor (`"AWS"`, `"Azure"`, `"GCP"`, etc.), lo que permite filtrar y agrupar por proveedor en tus dashboards.

---

## Actividades (trazas) disponibles

Las actividades se publican bajo el `ActivitySource` llamado `"ValiBlob"`. Cada operación crea una actividad de tipo `ActivityKind.Client`.

| Nombre de actividad | Descripción |
|---|---|
| `storage.upload` | Operación de upload |
| `storage.download` | Operación de download |
| `storage.delete` | Operación de delete |
| `storage.exists` | Verificación de existencia |
| `storage.copy` | Operación de copia |
| `storage.move` | Operación de movimiento |
| `storage.list` | Listado de archivos |
| `storage.getmetadata` | Obtención de metadata |

### Tags de actividades

Cada actividad incluye los siguientes tags:

| Tag | Valor de ejemplo | Descripción |
|---|---|---|
| `storage.provider` | `"AWS"` | Nombre del proveedor |
| `storage.path` | `"documentos/factura.pdf"` | Ruta del archivo |
| `storage.operation` | `"upload"` | Nombre de la operación |

---

## Configuración con OpenTelemetry SDK

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.Console
```

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ValiBlob.Core.Telemetry;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(StorageTelemetry.MeterName) // "ValiBlob"
            .AddConsoleExporter(); // reemplazá por tu exporter en producción
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(StorageTelemetry.ActivitySourceName) // "ValiBlob"
            .AddConsoleExporter();
    });
```

---

## Configuración con Prometheus

```bash
dotnet add package OpenTelemetry.Exporter.Prometheus.AspNetCore
```

```csharp
using OpenTelemetry.Metrics;
using ValiBlob.Core.Telemetry;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(StorageTelemetry.MeterName)
            .AddPrometheusExporter();
    });

// En el pipeline de la app
var app = builder.Build();
app.MapPrometheusScrapingEndpoint(); // expone /metrics
app.Run();
```

Con esta configuración, Prometheus puede scrapear el endpoint `/metrics` de tu aplicación. Las métricas aparecerán con nombres como:

```
valiblob_uploads_total{provider="AWS"} 42
valiblob_uploads_bytes_total{provider="AWS"} 104857600
valiblob_upload_duration_ms_bucket{provider="AWS",le="100"} 38
```

### Ejemplo de query PromQL

```promql
# Tasa de uploads por proveedor (por minuto)
rate(valiblob_uploads_total[1m])

# Percentil 95 de duración de uploads
histogram_quantile(0.95, rate(valiblob_upload_duration_ms_bucket[5m]))

# Total de bytes subidos en la última hora
increase(valiblob_uploads_bytes_total[1h])
```

---

## Configuración con Azure Monitor / Application Insights

```bash
dotnet add package Azure.Monitor.OpenTelemetry.AspNetCore
```

```csharp
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ValiBlob.Core.Telemetry;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(StorageTelemetry.MeterName);
    })
    .WithTracing(tracing =>
    {
        tracing.AddSource(StorageTelemetry.ActivitySourceName);
    })
    .UseAzureMonitor(options =>
    {
        options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
    });
```

En Application Insights, las trazas de ValiBlob aparecerán como dependencias de tipo `"storage"` en el mapa de aplicación. Las métricas estarán disponibles en la sección "Metrics" con el prefijo `valiblob_`.

---

## Configuración con OTLP (Jaeger, Grafana Tempo, etc.)

```bash
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ValiBlob.Core.Telemetry;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(StorageTelemetry.MeterName)
            .AddOtlpExporter(opts =>
            {
                opts.Endpoint = new Uri("http://otel-collector:4317");
            });
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(StorageTelemetry.ActivitySourceName)
            .AddOtlpExporter(opts =>
            {
                opts.Endpoint = new Uri("http://otel-collector:4317");
            });
    });
```

---

## Desactivar telemetría

Si no usás OpenTelemetry en tu proyecto y querés evitar el overhead (mínimo, pero presente):

```json
{
  "ValiBlob": {
    "EnableTelemetry": false
  }
}
```
