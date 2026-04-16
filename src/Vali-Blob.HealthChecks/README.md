# Vali-Blob.HealthChecks

[![NuGet](https://img.shields.io/nuget/v/ValiBlob.HealthChecks.svg)](https://www.nuget.org/packages/ValiBlob.HealthChecks)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](https://github.com/UBF21/Vali-Blob/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6%20%7C%207%20%7C%208%20%7C%209-purple)](https://www.nuget.org/packages/ValiBlob.HealthChecks)

ASP.NET Core Health Checks integration for **Vali-Blob** — the unified cloud storage abstraction library for .NET.

Registers one health check per configured provider that probes the underlying storage service (bucket / container existence, connectivity, credentials). Integrates with the standard `Microsoft.Extensions.Diagnostics.HealthChecks` infrastructure — compatible with `/health` endpoints, health check UIs, Kubernetes liveness/readiness probes, and monitoring dashboards.

---

## Compatibility

| Target Framework | Supported |
|---|---|
| `netstandard2.0` | Yes |
| `netstandard2.1` | Yes |
| `net6.0` | Yes |
| `net7.0` | Yes |
| `net8.0` | Yes |
| `net9.0` | Yes |

---

## Installation

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.HealthChecks
```

---

## Registration

### Basic setup

```csharp
using ValiBlob.HealthChecks.Extensions;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseAWS()
    .UseAzure();

builder.Services
    .AddHealthChecks()
    .AddValiBlob("AWS")
    .AddValiBlob("Azure");

app.MapHealthChecks("/health");
```

### With tags (recommended)

Use tags to separate critical checks from optional ones and expose them on different endpoints:

```csharp
builder.Services
    .AddHealthChecks()
    .AddValiBlob("AWS",      tags: new[] { "storage", "live" })
    .AddValiBlob("Azure",    tags: new[] { "storage", "live" })
    .AddValiBlob("GCP",      tags: new[] { "storage", "optional" });

// Liveness — only storage checks
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

// Readiness — all checks
app.MapHealthChecks("/health/ready");
```

### Custom check name and timeout

```csharp
builder.Services
    .AddHealthChecks()
    .AddValiBlob(
        providerKey:  "AWS",
        name:         "s3-primary",
        failureStatus: HealthStatus.Degraded,
        tags:          new[] { "storage" },
        timeout:       TimeSpan.FromSeconds(5)
    );
```

---

## JSON response

```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.2134821",
  "entries": {
    "valiblob-AWS": {
      "status": "Healthy",
      "description": "AWS S3 bucket reachable",
      "duration": "00:00:00.1234567"
    },
    "valiblob-Azure": {
      "status": "Healthy",
      "description": "Azure Blob container reachable",
      "duration": "00:00:00.0987654"
    },
    "valiblob-GCP": {
      "status": "Unhealthy",
      "description": "GCP: Access denied — check IAM permissions",
      "duration": "00:00:00.0500123"
    }
  }
}
```

---

## Kubernetes integration

```yaml
livenessProbe:
  httpGet:
    path: /health/live
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 30

readinessProbe:
  httpGet:
    path: /health/ready
    port: 8080
  initialDelaySeconds: 5
  periodSeconds: 10
```

---

## Health check UI

Compatible with [AspNetCore.HealthChecks.UI](https://github.com/Xabaril/AspNetCore.HealthChecks.UI):

```csharp
builder.Services
    .AddHealthChecksUI()
    .AddInMemoryStorage();

app.MapHealthChecksUI(opts => opts.UIPath = "/health-ui");
```

---

## Features

| Feature | Supported |
|---|---|
| Per-provider health checks | Yes |
| Custom check name | Yes |
| Tags for endpoint filtering | Yes |
| Custom failure status (Degraded/Unhealthy) | Yes |
| Custom timeout per check | Yes |
| Kubernetes liveness/readiness compatible | Yes |
| Health check UI compatible | Yes |
| Works with all Vali-Blob providers | Yes |

---

## Troubleshooting

If a provider reports `Unhealthy`, inspect the application logs under the `ValiBlob.HealthChecks` category — the underlying exception is logged at `Error` level.

Common causes:

| Symptom | Likely cause |
|---|---|
| `Access denied` / `403 Forbidden` | Insufficient IAM / RBAC permissions |
| `Bucket/container not found` | Wrong bucket name or region in configuration |
| `Connection refused` | Network policy blocking outbound calls |
| `Invalid credentials` | Expired or incorrect API key / secret |

See the [Troubleshooting guide](https://vali-blob-docs.netlify.app/docs/troubleshooting) for the full diagnostic reference.

---

## Documentation

- [Health checks docs](https://vali-blob-docs.netlify.app/docs/health-checks)
- [Full documentation](https://vali-blob-docs.netlify.app)

---

## Links

- [GitHub Repository](https://github.com/UBF21/Vali-Blob)
- [NuGet Package](https://www.nuget.org/packages/ValiBlob.HealthChecks)

---

## Donations

If Vali-Blob is useful to you, consider supporting its development:

- **Latin America** — [MercadoPago](https://link.mercadopago.com.pe/felipermm)
- **International** — [PayPal](https://paypal.me/felipeRMM?country.x=PE&locale.x=es_XC)

---

## License

[MIT License](https://github.com/UBF21/Vali-Blob/blob/main/LICENSE)

## Contributions

Issues and pull requests are welcome on [GitHub](https://github.com/UBF21/Vali-Blob).
