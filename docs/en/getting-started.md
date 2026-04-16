# Getting Started

This guide walks you through installing ValiBlob, wiring it into an ASP.NET Core application, and performing your first upload, download, and delete operations.

---

## Prerequisites

- .NET 6, 7, 8, or 9 (or .NET Standard 2.0 / 2.1 for library projects)
- An ASP.NET Core or console application with a `ServiceCollection`
- A cloud storage account (AWS, Azure, GCP, OCI, or Supabase) — or use `ValiBlob.Testing` for fully in-memory operation

---

## Installation

Install the core package first, then add the provider(s) you need.

### Core package (required)

```bash
dotnet add package ValiBlob.Core
```

### Provider packages (choose one or more)

```bash
# Amazon S3 (also works with MinIO)
dotnet add package ValiBlob.AWS

# Azure Blob Storage
dotnet add package ValiBlob.Azure

# Google Cloud Storage
dotnet add package ValiBlob.GCP

# Oracle Cloud Infrastructure Object Storage
dotnet add package ValiBlob.OCI

# Supabase Storage
dotnet add package ValiBlob.Supabase
```

### Optional packages

```bash
# ASP.NET Core health check endpoints
dotnet add package ValiBlob.HealthChecks

# In-memory provider for unit/integration tests (add to test projects only)
dotnet add package ValiBlob.Testing
```

---

## Basic setup

### `Program.cs` (minimal API / .NET 6/7+)

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.AWS.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddValiBlob(opts =>
    {
        // Which provider to resolve when IStorageProvider is injected directly
        opts.DefaultProvider = "AWS";
        opts.EnableTelemetry = true;
        opts.EnableLogging = true;
    })
    .UseAWS()
    .WithPipeline(p => p
        .UseValidation()
        .UseCompression()
    )
    .WithResiliencePolicies(r =>
    {
        r.RetryCount = 3;
        r.UseExponentialBackoff = true;
    });

var app = builder.Build();
app.Run();
```

### `Startup.cs` (.NET Core 3.1 / .NET 5 style)

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Azure.Extensions;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddValiBlob()
            .UseAzure()
            .WithDefaultProvider("Azure");
    }
}
```

---

## `appsettings.json` full example

The configuration section is `ValiBlob`. Each provider reads its own sub-section.

```json
{
  "ValiBlob": {
    "DefaultProvider": "AWS",
    "EnableTelemetry": true,
    "EnableLogging": true,

    "Resilience": {
      "RetryCount": 3,
      "RetryDelay": "00:00:01",
      "UseExponentialBackoff": true,
      "CircuitBreakerThreshold": 5,
      "CircuitBreakerDuration": "00:00:30",
      "Timeout": "00:01:00"
    },

    "Validation": {
      "MaxFileSizeBytes": 52428800,
      "AllowedExtensions": [ ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".docx", ".xlsx" ],
      "BlockedExtensions": [ ".exe", ".bat", ".cmd", ".sh" ],
      "AllowedContentTypes": []
    },

    "Compression": {
      "Enabled": true,
      "MinSizeBytes": 1024,
      "CompressibleContentTypes": [
        "text/plain", "text/html", "text/css", "text/xml",
        "application/json", "application/xml", "application/javascript"
      ]
    }
  },

  "ValiBlob:AWS": {
    "Bucket": "my-app-files",
    "Region": "us-east-1",
    "AccessKeyId": "AKIAIOSFODNN7EXAMPLE",
    "SecretAccessKey": "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
    "UseIAMRole": false,
    "MultipartThresholdMb": 100,
    "MultipartChunkSizeMb": 8
  },

  "ValiBlob:Azure": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=base64key==;EndpointSuffix=core.windows.net",
    "Container": "my-container",
    "CreateContainerIfNotExists": true
  },

  "ValiBlob:GCP": {
    "Bucket": "my-gcp-bucket",
    "ProjectId": "my-gcp-project",
    "CredentialsPath": "/etc/gcp/service-account.json"
  },

  "ValiBlob:OCI": {
    "Namespace": "my-tenancy-namespace",
    "Bucket": "my-oci-bucket",
    "Region": "sa-saopaulo-1",
    "TenancyId": "ocid1.tenancy.oc1..aaa...",
    "UserId": "ocid1.user.oc1..aaa...",
    "Fingerprint": "aa:bb:cc:dd:ee:ff:00:11:22:33:44:55:66:77:88:99",
    "PrivateKeyPath": "/etc/oci/oci_api_key.pem"
  },

  "ValiBlob:Supabase": {
    "Url": "https://xyzcompany.supabase.co",
    "ApiKey": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "Bucket": "public"
  }
}
```

> **⚠️ Warning:** Never commit `AccessKeyId`, `SecretAccessKey`, `AccountKey`, `ApiKey`, or private key content to source control. Use environment variables, Azure Key Vault, AWS Secrets Manager, or `dotnet user-secrets` for local development.

---

## First upload

Inject `IStorageProvider` into any service or controller.

```csharp
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;

public class DocumentService
{
    private readonly IStorageProvider _storage;

    public DocumentService(IStorageProvider storage)
    {
        _storage = storage;
    }

    public async Task<UploadResult> UploadDocumentAsync(
        Stream documentStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var path = StoragePath.From("documents", DateTime.UtcNow.Year.ToString(), fileName);

        var request = new UploadRequest
        {
            Path = path,
            Content = documentStream,
            ContentType = contentType,
            ContentLength = documentStream.CanSeek ? documentStream.Length : null,
            Metadata = new Dictionary<string, string>
            {
                ["uploaded-by"] = "document-service",
                ["environment"] = "production"
            }
        };

        // Track upload progress
        var progress = new Progress<UploadProgress>(p =>
            Console.WriteLine($"Upload progress: {p}"));

        var result = await _storage.UploadAsync(request, progress, cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Upload failed [{result.ErrorCode}]: {result.ErrorMessage}");
        }

        return result.Value!;
    }
}
```

---

## First download

```csharp
public async Task<byte[]> DownloadDocumentAsync(
    string documentPath,
    CancellationToken cancellationToken = default)
{
    var result = await _storage.DownloadAsync(new DownloadRequest
    {
        Path = StoragePath.From(documentPath)
    }, cancellationToken);

    if (!result.IsSuccess)
    {
        if (result.ErrorCode == StorageErrorCode.FileNotFound)
            throw new FileNotFoundException($"Document not found: {documentPath}");

        throw new InvalidOperationException(
            $"Download failed [{result.ErrorCode}]: {result.ErrorMessage}");
    }

    using var memoryStream = new MemoryStream();
    await result.Value!.CopyToAsync(memoryStream, cancellationToken);
    return memoryStream.ToArray();
}
```

To stream a file directly to an HTTP response without buffering in memory:

```csharp
// ASP.NET Core controller
[HttpGet("files/{**path}")]
public async Task<IActionResult> GetFile(string path)
{
    var result = await _storage.DownloadAsync(new DownloadRequest
    {
        Path = StoragePath.From(path)
    });

    if (!result.IsSuccess)
        return NotFound(result.ErrorMessage);

    var metadata = await _storage.GetMetadataAsync(path);
    var contentType = metadata.Value?.ContentType ?? "application/octet-stream";

    return File(result.Value!, contentType, Path.GetFileName(path));
}
```

---

## First delete

```csharp
public async Task DeleteDocumentAsync(string path, CancellationToken cancellationToken = default)
{
    var result = await _storage.DeleteAsync(path, cancellationToken);

    if (!result.IsSuccess)
        throw new InvalidOperationException($"Delete failed: {result.ErrorMessage}");
}
```

---

## Common mistakes and troubleshooting

### "No provider registered with name 'X'"

Ensure you called `.UseAWS()` (or the appropriate provider extension) after `.AddValiBlob()`, and that `DefaultProvider` matches the registered key exactly (case-sensitive: `"AWS"`, `"Azure"`, `"GCP"`, `"OCI"`, `"Supabase"`, `"InMemory"`).

### Validation errors on upload

If the pipeline includes `.UseValidation()`, check:
- File extension is in `AllowedExtensions` (if the list is non-empty)
- File extension is not in `BlockedExtensions`
- File size is within `MaxFileSizeBytes`
- Content type is in `AllowedContentTypes` (if the list is non-empty)

The result's `ErrorCode` will be `StorageErrorCode.ValidationFailed` and `ErrorMessage` will explain which rule failed.

### AWS `InvalidAccessKeyId` error

The access key or secret is wrong. Double-check `appsettings.json` or environment variables. If running on EC2/ECS/Lambda, set `UseIAMRole: true` and remove the key fields.

### Azure `AuthenticationFailed`

Either the connection string is malformed or the account key has been rotated. When using `AccountName` + `AccountKey`, the key must be the Base64-encoded storage account key from the Azure portal.

### GCP `Could not load the default credentials`

Set `CredentialsPath` in `GCPStorageOptions`, or set the `GOOGLE_APPLICATION_CREDENTIALS` environment variable before the process starts.

### OCI `401 Unauthorized`

The fingerprint in `OCIStorageOptions` must exactly match the API key fingerprint shown in the OCI console. Verify the private key file path and ensure the key is not password-protected (or provide the passphrase if the SDK version supports it).

### Content-type is `null` after download

Not all providers store content type. Always set `ContentType` on `UploadRequest` to ensure it is preserved and can be returned on metadata retrieval.

### `StoragePath` throws `ArgumentException`

`StoragePath.From(...)` rejects empty or whitespace-only segments. Validate user input before constructing paths. Pre-joined strings like `"docs/2024/file.pdf"` are valid as a single argument.
