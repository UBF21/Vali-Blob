# ValiBlob.HealthChecks

ASP.NET Core health check integration for ValiBlob.

Adds a health check per registered provider that probes the underlying storage service (bucket existence, connectivity, credentials). Integrates with the standard `/healthz` endpoint and any health check UI.

## Install

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.HealthChecks
```

## Register

```csharp
using ValiBlob.HealthChecks.Extensions;

builder.Services
    .AddHealthChecks()
    .AddValiBlob("AWS")
    .AddValiBlob("Azure");

// Map the health endpoint
app.MapHealthChecks("/healthz");
```

## Tagging for conditional checks

```csharp
builder.Services
    .AddHealthChecks()
    .AddValiBlob("AWS",   tags: new[] { "storage", "required" })
    .AddValiBlob("GCP",   tags: new[] { "storage", "optional" });

// Expose only required checks on the main endpoint
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("required")
});

// Expose all checks on a separate endpoint
app.MapHealthChecks("/healthz/full");
```

## JSON response format

```json
{
  "status": "Healthy",
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
    }
  }
}
```

## Troubleshooting

If a provider reports `Unhealthy`, check the application logs under the `ValiBlob.HealthChecks` category for the underlying exception. Common causes: incorrect credentials, missing bucket/container, or insufficient IAM permissions. See [Troubleshooting](../../docs/en/troubleshooting.md) for the full diagnostic guide.

## Documentation

[Health checks docs](../../docs/en/health-checks.md)
