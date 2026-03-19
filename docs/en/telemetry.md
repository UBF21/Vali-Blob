# Telemetry

ValiBlob emits OpenTelemetry-compatible **metrics** and **distributed tracing activities** for every storage operation. Telemetry is enabled by default (`EnableTelemetry: true`) and integrates with any OpenTelemetry-compatible observability backend (Prometheus, Jaeger, Zipkin, Azure Monitor, Datadog, etc.).

---

## OpenTelemetry integration overview

ValiBlob uses:
- `System.Diagnostics.ActivitySource` for distributed tracing
- `System.Diagnostics.Metrics.Meter` for metrics

Both are exposed under the source/meter name `"ValiBlob"`.

---

## Available metrics

| Metric name | Type | Unit | Description |
|---|---|---|---|
| `valiblob.uploads.total` | Counter | `uploads` | Total number of upload operations |
| `valiblob.downloads.total` | Counter | `downloads` | Total number of download operations |
| `valiblob.deletes.total` | Counter | `deletes` | Total number of delete operations |
| `valiblob.uploads.bytes` | Counter | `bytes` | Total bytes uploaded |
| `valiblob.upload.duration_ms` | Histogram | `ms` | Upload operation duration in milliseconds |
| `valiblob.download.duration_ms` | Histogram | `ms` | Download operation duration in milliseconds |

All metrics include a `provider` tag whose value is the `ProviderName` of the storage provider (e.g., `"AWS"`, `"Azure"`, `"GCP"`).

---

## Available activity names

Activities (spans) are started using `ActivitySource.StartActivity(name, ActivityKind.Client)`.

The activity name format is `storage.{operationName}`:

| Activity name | Triggered by |
|---|---|
| `storage.upload` | `UploadAsync` |
| `storage.download` | `DownloadAsync` |
| `storage.delete` | `DeleteAsync` |
| `storage.exists` | `ExistsAsync` |
| `storage.geturl` | `GetUrlAsync` |
| `storage.copy` | `CopyAsync` |
| `storage.move` | `MoveAsync` |
| `storage.getmetadata` | `GetMetadataAsync` |
| `storage.setmetadata` | `SetMetadataAsync` |
| `storage.listfiles` | `ListFilesAsync` |

---

## Activity tags reference

Every activity started by ValiBlob includes the following tags:

| Tag | Value |
|---|---|
| `storage.provider` | Provider name (e.g., `"AWS"`) |
| `storage.path` | Object path of the operation |
| `storage.operation` | Operation name (e.g., `"upload"`) |

---

## Wiring with OpenTelemetry SDK

Install the OpenTelemetry SDK and instrument it to listen to ValiBlob's source:

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Exporter.Console   # for local testing
```

```csharp
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("my-app"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddSource("ValiBlob")           // subscribe to ValiBlob activities
        .AddConsoleExporter())           // replace with your exporter
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter("ValiBlob")            // subscribe to ValiBlob metrics
        .AddConsoleExporter());          // replace with your exporter
```

---

## Wiring with Prometheus

```bash
dotnet add package OpenTelemetry.Exporter.Prometheus.AspNetCore
```

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("my-app"))
    .WithMetrics(metrics => metrics
        .AddMeter("ValiBlob")
        .AddPrometheusExporter());

// Expose /metrics endpoint
app.UseOpenTelemetryPrometheusScrapingEndpoint();
```

After adding the exporter, the following metrics are available at `/metrics`:

```
# HELP valiblob_uploads_total Total number of upload operations
# TYPE valiblob_uploads_total counter
valiblob_uploads_total{provider="AWS"} 142

# HELP valiblob_upload_duration_ms Upload operation duration in milliseconds
# TYPE valiblob_upload_duration_ms histogram
valiblob_upload_duration_ms_bucket{provider="AWS",le="100"} 98
valiblob_upload_duration_ms_bucket{provider="AWS",le="500"} 135
```

---

## Wiring with Azure Monitor / Application Insights

```bash
dotnet add package Azure.Monitor.OpenTelemetry.AspNetCore
```

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("my-app"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddSource("ValiBlob"))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter("ValiBlob"))
    .UseAzureMonitor(o =>
    {
        o.ConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
    });
```

ValiBlob activities will appear as dependency calls in the Application Insights transaction search with the `storage.*` operation names.

---

## Disabling telemetry

If you do not need telemetry and want to avoid the overhead of creating activities that are never listened to, set `EnableTelemetry: false`:

```json
{
  "ValiBlob": {
    "EnableTelemetry": false
  }
}
```

Or via code:

```csharp
builder.Services.AddValiBlob(opts => opts.EnableTelemetry = false);
```

> **💡 Tip:** Even with telemetry enabled, if no listener is subscribed to the `"ValiBlob"` `ActivitySource`, the cost of creating activities is negligible — the SDK short-circuits when there are no listeners. Disabling is only necessary if you have profiling evidence of overhead.
