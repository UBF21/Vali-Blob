# ValiBlob

**Cloud storage abstraction for .NET**

![Build](https://img.shields.io/badge/build-passing-brightgreen)
![NuGet](https://img.shields.io/badge/nuget-1.0.0-blue)
![License](https://img.shields.io/badge/license-MIT-green)
![.NET](https://img.shields.io/badge/.NET-6%20%7C%207%20%7C%208%20%7C%209%20%7C%20standard2.0%2F2.1-purple)
[![codecov](https://codecov.io/gh/YOUR_ORG/Vali-Blob/branch/main/graph/badge.svg)](https://codecov.io/gh/YOUR_ORG/Vali-Blob)

---

## What is ValiBlob?

ValiBlob is a .NET library that provides a unified `IStorageProvider` abstraction over multiple cloud storage backends, including AWS S3, Azure Blob Storage, Google Cloud Storage, Oracle Cloud Infrastructure, and Supabase Storage. It lets you write storage logic once and swap providers through configuration, without changing application code. The library is designed to integrate naturally into Clean Architecture, Hexagonal, and Onion Architecture patterns.

---

## Features

- **Multi-provider support** — AWS S3, Azure Blob, GCP, OCI, Supabase, and in-memory for testing
- **Middleware pipeline** — composable pipeline with built-in `ValidationMiddleware`, `CompressionMiddleware`, and `EncryptionMiddleware`
- **Resumable uploads** — TUS-style chunked upload support via `IResumableUploadProvider` with MD5 checksum validation
- **Presigned URLs** — generate time-limited download and upload URLs on all supported providers
- **Health checks** — ready-to-use ASP.NET Core health check integration
- **OpenTelemetry** — built-in `ActivitySource` and `Meter` with operation counters and latency histograms
- **Event system** — `StorageEventDispatcher` for reacting to upload, download, and delete events
- **Resilience** — Polly-based retry, circuit breaker, and timeout policies out of the box
- **Testing utilities** — `InMemoryStorageProvider` in `ValiBlob.Testing` for fast, hermetic unit tests
- **`StorageResult<T>`** — discriminated result type for explicit, exception-free error handling

---

## Supported Providers

| Provider                        | Package             | Status     |
|---------------------------------|---------------------|------------|
| AWS S3                          | `ValiBlob.AWS`      | Stable |
| Azure Blob Storage              | `ValiBlob.Azure`    | Stable |
| Google Cloud Storage            | `ValiBlob.GCP`      | Stable |
| Oracle Cloud Infrastructure     | `ValiBlob.OCI`      | Stable |
| Supabase Storage                | `ValiBlob.Supabase` | Stable |
| In-Memory (testing)             | `ValiBlob.Testing`  | Stable |

---

## Quick Start

Install the core package and the provider of your choice:

```
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.AWS
```

Register ValiBlob in your DI container:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS(options =>
    {
        options.BucketName = "my-bucket";
        options.Region     = "us-east-1";
    });
```

Upload and download a file:

```csharp
public class FileService(IStorageProvider storage)
{
    public async Task UploadAsync(Stream content, string fileName)
    {
        StorageResult<string> result = await storage.UploadAsync(
            new UploadRequest(fileName, content));

        if (result.IsSuccess)
            Console.WriteLine($"Stored at: {result.Value}");
        else
            Console.WriteLine($"Error: {result.ErrorMessage}");
    }

    public async Task<Stream?> DownloadAsync(string fileName)
    {
        StorageResult<Stream> result = await storage.DownloadAsync(fileName);
        return result.IsSuccess ? result.Value : null;
    }
}
```

---

## Packages

| Package                  | Description                                              |
|--------------------------|----------------------------------------------------------|
| `ValiBlob.Core`          | Core abstractions, middleware pipeline, events, telemetry |
| `ValiBlob.AWS`           | AWS S3 provider implementation                           |
| `ValiBlob.Azure`         | Azure Blob Storage provider implementation               |
| `ValiBlob.GCP`           | Google Cloud Storage provider implementation             |
| `ValiBlob.OCI`           | Oracle Cloud Infrastructure provider implementation      |
| `ValiBlob.Supabase`      | Supabase Storage provider implementation                 |
| `ValiBlob.HealthChecks`  | ASP.NET Core health check integration                    |
| `ValiBlob.Testing`       | InMemoryStorageProvider and test helpers                 |

---

## Documentation

Full documentation lives in the `docs/` directory:

- English: [`docs/en/`](docs/en/)
- Spanish: [`docs/es/`](docs/es/)

Topics covered include provider configuration, middleware authoring, resumable uploads, presigned URLs, OpenTelemetry setup, resilience policies, and the testing guide.

---

## Architecture

ValiBlob is designed as an infrastructure-layer library compatible with Clean Architecture, Hexagonal (Ports & Adapters), and Onion Architecture. The `IStorageProvider` interface is your port; each provider package is an adapter. The core package contains no cloud SDK dependencies — only the abstractions, pipeline infrastructure, and cross-cutting concerns, so your domain and application layers remain free of vendor coupling.

---

## License

ValiBlob is released under the [MIT License](LICENSE).
