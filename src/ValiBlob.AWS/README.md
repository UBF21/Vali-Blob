# ValiBlob.AWS

AWS S3 and MinIO provider for ValiBlob.

Supports standard S3 uploads, automatic multipart for large files, presigned URLs, resumable uploads, and full compatibility with MinIO self-hosted deployments.

## Install

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.AWS
```

## Configuration

```json
{
  "ValiBlob": {
    "DefaultProvider": "AWS"
  },
  "ValiBlob:AWS": {
    "Bucket":          "my-app-files",
    "Region":          "us-east-1",
    "AccessKeyId":     "",
    "SecretAccessKey": ""
  }
}
```

Set `AccessKeyId` and `SecretAccessKey` via environment variables or a secrets manager — never commit them to source control. On EC2/ECS/Lambda, leave them blank and the SDK will use the instance profile automatically.

## Register

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.AWS.Extensions;

// AWS S3
builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseAWS();

// MinIO
builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseMinIO(opts =>
    {
        opts.Endpoint        = "http://localhost:9000";
        opts.AccessKeyId     = "minioadmin";
        opts.SecretAccessKey = "minioadmin";
        opts.Bucket          = "my-bucket";
    });
```

## Features

| Feature | Supported |
|---|---|
| Upload / Download / Delete / List | Yes |
| Automatic multipart (large files) | Yes |
| Presigned upload URL | Yes |
| Presigned download URL | Yes |
| Resumable uploads (chunked) | Yes |
| BucketOverride per request | Yes |
| MinIO compatibility | Yes |

## Documentation

[AWS S3 / MinIO provider docs](../../docs/en/providers/aws.md)
