# Health Checks

The `ValiBlob.HealthChecks` package integrates with the ASP.NET Core Health Checks framework, allowing you to verify that your configured storage provider is reachable as part of standard `/healthz` or `/health` endpoint monitoring.

---

## Installation

```bash
dotnet add package ValiBlob.HealthChecks
```

---

## How it works

The health check performs a `ListFilesAsync` call against the provider using the configured `ProbePrefix`. If the call succeeds, the check reports `Healthy`. If it throws or returns a failure result, the check reports `Unhealthy` (or the configured `failureStatus`).

---

## Basic usage — default provider

Register a health check for the default storage provider:

```csharp
using ValiBlob.HealthChecks.Extensions;

builder.Services.AddHealthChecks()
    .AddValiBlob();
```

This registers a check named `"valiblob"` that probes the provider configured as `DefaultProvider`.

---

## Checking a specific provider by name

When multiple providers are registered, check each one by name:

```csharp
builder.Services.AddHealthChecks()
    .AddValiBlob("AWS",      checkName: "storage-aws")
    .AddValiBlob("Azure",    checkName: "storage-azure")
    .AddValiBlob("Supabase", checkName: "storage-supabase");
```

The first overload of `AddValiBlob` with a `providerName` parameter resolves the provider via `IStorageFactory.Create(providerName)`.

---

## `StorageHealthCheckOptions` reference

| Property | Type | Default | Description |
|---|---|---|---|
| `ProbePrefix` | `string?` | `null` | Path prefix used in the probe `ListFilesAsync` call. `null` lists from the root. |
| `Timeout` | `TimeSpan` | `00:00:05` (5s) | Maximum time the health check waits before reporting unhealthy |

---

## Configuring `StorageHealthCheckOptions`

```csharp
builder.Services.AddHealthChecks()
    .AddValiBlob(configure: opts =>
    {
        opts.ProbePrefix = "health-probe/";  // List files under this prefix only
        opts.Timeout = TimeSpan.FromSeconds(3);
    });
```

Using a dedicated probe prefix is recommended for production: create an empty "sentinel" file at the probe path so the list returns at least one result, which gives higher confidence the bucket is accessible.

---

## Integrating with the `/healthz` endpoint

```csharp
var app = builder.Build();

app.MapHealthChecks("/healthz");

// Or with detailed output (JSON format)
app.MapHealthChecks("/healthz/detail", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.Run();
```

> **💡 Tip:** Install `AspNetCore.HealthChecks.UI.Client` for the JSON response writer that produces machine-readable health check output with individual check details.

---

## Full example — multiple providers

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.AWS.Extensions;
using ValiBlob.Azure.Extensions;
using ValiBlob.HealthChecks.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Register providers
builder.Services
    .AddValiBlob(o => o.DefaultProvider = "AWS")
    .UseAWS()
    .UseAzure();

// Register health checks
builder.Services.AddHealthChecks()
    .AddValiBlob(
        "AWS",
        checkName: "storage-s3",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "storage", "aws" },
        configure: opts =>
        {
            opts.ProbePrefix = "health/";
            opts.Timeout = TimeSpan.FromSeconds(5);
        })
    .AddValiBlob(
        "Azure",
        checkName: "storage-azure",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "storage", "azure" },
        configure: opts =>
        {
            opts.Timeout = TimeSpan.FromSeconds(5);
        });

var app = builder.Build();

app.MapHealthChecks("/healthz");
app.MapHealthChecks("/healthz/storage", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("storage")
});

app.Run();
```

Sample `/healthz` JSON response:

```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.0823456",
  "entries": {
    "storage-s3": {
      "data": {},
      "duration": "00:00:00.0512341",
      "status": "Healthy",
      "tags": ["storage", "aws"]
    },
    "storage-azure": {
      "data": {},
      "duration": "00:00:00.0311115",
      "status": "Healthy",
      "tags": ["storage", "azure"]
    }
  }
}
```

---

## Using with Kubernetes liveness and readiness probes

Map health checks to separate endpoints for Kubernetes:

```csharp
// Liveness — is the app process alive?
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false  // no checks, just 200 OK
});

// Readiness — can the app handle traffic (storage reachable)?
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("storage")
});
```

```yaml
# Kubernetes deployment snippet
livenessProbe:
  httpGet:
    path: /health/live
    port: 8080
  initialDelaySeconds: 5
  periodSeconds: 10

readinessProbe:
  httpGet:
    path: /health/ready
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 30
```
