# Azure Blob Storage Provider

The `ValiBlob.Azure` package wraps the official `Azure.Storage.Blobs` SDK and provides full `IStorageProvider` support for Azure Blob Storage.

---

## Installation

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.Azure
```

---

## Authentication options

### Connection string (simplest)

Obtain your storage account connection string from the Azure portal under **Storage account → Access keys**.

```json
{
  "ValiBlob:Azure": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=BASE64KEY==;EndpointSuffix=core.windows.net",
    "Container": "my-container"
  }
}
```

### Account name + account key

If you prefer to store the account name and key separately:

```json
{
  "ValiBlob:Azure": {
    "AccountName": "myaccount",
    "AccountKey": "BASE64ACCOUNTKEY==",
    "Container": "my-container"
  }
}
```

When both `ConnectionString` and `AccountName`/`AccountKey` are present, `ConnectionString` takes precedence.

> **⚠️ Warning:** If neither `ConnectionString` nor `AccountName` + `AccountKey` is provided, the provider throws `InvalidOperationException` at startup time with a descriptive message.

### Azure Managed Identity / DefaultAzureCredential

The underlying `BlobServiceClient` can be constructed with `DefaultAzureCredential` for passwordless authentication when running on Azure. To do this, configure via code:

```csharp
using Azure.Identity;
using Azure.Storage.Blobs;

builder.Services
    .AddValiBlob()
    .UseAzure();

// Override the BlobServiceClient registration with DefaultAzureCredential
builder.Services.AddSingleton(_ =>
    new BlobServiceClient(
        new Uri("https://myaccount.blob.core.windows.net"),
        new DefaultAzureCredential()));
```

> **💡 Tip:** Managed Identity is the recommended approach for production deployments on Azure App Service, AKS, or Azure Functions.

---

## Full `AzureBlobOptions` reference

| Property | Type | Default | Description |
|---|---|---|---|
| `ConnectionString` | `string?` | `null` | Full Azure Storage connection string |
| `AccountName` | `string?` | `null` | Storage account name (used with `AccountKey`) |
| `AccountKey` | `string?` | `null` | Storage account key (Base64) |
| `Container` | `string` | `""` | Default blob container name |
| `CdnBaseUrl` | `string?` | `null` | CDN prefix substituted in `GetUrlAsync` responses |
| `CreateContainerIfNotExists` | `bool` | `true` | Automatically creates the container if it does not exist |

Configuration section: `ValiBlob:Azure`

---

## `appsettings.json` example

```json
{
  "ValiBlob": {
    "DefaultProvider": "Azure"
  },
  "ValiBlob:Azure": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=BASE64KEY==;EndpointSuffix=core.windows.net",
    "Container": "app-files",
    "CdnBaseUrl": "https://my-cdn.azureedge.net",
    "CreateContainerIfNotExists": true
  }
}
```

---

## Code-only configuration

```csharp
builder.Services
    .AddValiBlob(o => o.DefaultProvider = "Azure")
    .UseAzure(opts =>
    {
        opts.ConnectionString = builder.Configuration.GetConnectionString("AzureStorage");
        opts.Container = "app-files";
        opts.CdnBaseUrl = "https://my-cdn.azureedge.net";
        opts.CreateContainerIfNotExists = true;
    });
```

---

## Container auto-creation

When `CreateContainerIfNotExists` is `true` (the default), ValiBlob checks whether the container exists before the first upload and creates it with private access if it does not. This happens once per provider instance lifetime.

Set it to `false` if container creation should be handled separately (e.g., via Terraform or Bicep infrastructure-as-code) to avoid the provider requiring `Storage Blob Data Contributor` role or above at startup.

---

## SAS tokens (presigned URLs)

The Azure provider implements `IPresignedUrlProvider` for generating time-limited Shared Access Signature (SAS) URLs.

```csharp
if (_storage is IPresignedUrlProvider sasProvider)
{
    // Generate a download SAS valid for 2 hours
    var downloadResult = await sasProvider.GetPresignedDownloadUrlAsync(
        "documents/report.pdf",
        expiration: TimeSpan.FromHours(2));

    if (downloadResult.IsSuccess)
        Console.WriteLine($"Download URL: {downloadResult.Value}");

    // Generate an upload SAS valid for 15 minutes
    var uploadResult = await sasProvider.GetPresignedUploadUrlAsync(
        "uploads/new-file.pdf",
        expiration: TimeSpan.FromMinutes(15));

    if (uploadResult.IsSuccess)
        Console.WriteLine($"Upload URL: {uploadResult.Value}");
}
```

SAS tokens use the `BlobSasPermissions` appropriate for the operation (read for download, write for upload) and are signed with the account key.

---

## CDN configuration

When `CdnBaseUrl` is configured, the `GetUrlAsync` method returns the CDN URL rather than the direct blob endpoint.

```json
"CdnBaseUrl": "https://my-cdn.azureedge.net"
```

```csharp
var result = await _storage.GetUrlAsync("images/banner.jpg");
// Returns: "https://my-cdn.azureedge.net/images/banner.jpg"
```

The CDN origin should point to your storage account's blob endpoint. Ensure your CDN rules allow the appropriate HTTP methods for your use case.

---

## BucketOverride

In Azure Blob Storage, containers are the equivalent of S3 buckets. Use `BucketOverride` on `UploadRequest` or `DownloadRequest` to target a different container for that operation.

```csharp
var result = await _storage.UploadAsync(new UploadRequest
{
    Path = StoragePath.From("reports", "q4-2024.xlsx"),
    Content = stream,
    ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    BucketOverride = "tenant-acme-corp"  // container override
});
```

> **💡 Tip:** When using `BucketOverride` with containers that may not exist yet, consider enabling `CreateContainerIfNotExists` or pre-creating containers in your tenant provisioning workflow.

---

## Limitations

- `SetMetadataAsync` performs an in-place metadata update using `BlobClient.SetMetadataAsync`. This is a single atomic operation and does not require re-uploading.
- Azure Blob Storage limits metadata key names to ASCII characters and requires they conform to C# identifier naming rules (no spaces, no leading digits).
- SAS URL generation requires the `BlobServiceClient` to be created with a `StorageSharedKeyCredential` (account key). SAS tokens cannot be generated client-side with `DefaultAzureCredential` / Managed Identity — use the Azure Blob SAS delegation API separately in that case.
- Container names must be between 3 and 63 characters, lowercase, and can only contain letters, digits, and hyphens.
