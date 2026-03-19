# Proveedor AWS S3 / MinIO

Este documento cubre la configuración del proveedor `ValiBlob.AWS`, que da soporte a Amazon S3 y a cualquier servicio compatible con la API S3 como MinIO.

---

## Instalación

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.AWS
```

---

## Opciones de autenticación

### Opción 1: IAM Role (recomendado para producción en AWS)

Cuando tu aplicación corre en EC2, ECS, Lambda o cualquier servicio con un IAM Role asignado, no necesitás credenciales explícitas. El SDK las obtiene automáticamente del metadata service de AWS.

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS(opts =>
    {
        opts.Bucket = "mi-bucket";
        opts.Region = "us-east-1";
        opts.UseIAMRole = true; // no se requieren AccessKeyId ni SecretAccessKey
    })
    .WithDefaultProvider("AWS");
```

```json
{
  "ValiBlob:AWS": {
    "Bucket": "mi-bucket",
    "Region": "us-east-1",
    "UseIAMRole": true
  }
}
```

### Opción 2: Access Key + Secret (desarrollo y ambientes sin IAM)

```json
{
  "ValiBlob:AWS": {
    "Bucket": "mi-bucket",
    "Region": "us-east-1",
    "AccessKeyId": "AKIAIOSFODNN7EXAMPLE",
    "SecretAccessKey": "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
  }
}
```

> **⚠️ Advertencia:** Nunca incluyas `AccessKeyId` ni `SecretAccessKey` en el código fuente ni en archivos que se versionen. Usá `dotnet user-secrets` para desarrollo local:
> ```bash
> dotnet user-secrets set "ValiBlob:AWS:AccessKeyId" "AKIAIOSFODNN7EXAMPLE"
> dotnet user-secrets set "ValiBlob:AWS:SecretAccessKey" "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
> ```

### Opción 3: Variables de entorno

El SDK de AWS también lee las variables de entorno estándar cuando no se proveen credenciales explícitas y `UseIAMRole` es `false`:

```bash
export AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE
export AWS_SECRET_ACCESS_KEY=wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY
export AWS_DEFAULT_REGION=us-east-1
```

---

## Referencia completa de `AWSS3Options`

| Propiedad | Tipo | Default | Descripción |
|---|---|---|---|
| `Bucket` | `string` | `""` | Nombre del bucket S3 por defecto |
| `Region` | `string` | `"us-east-1"` | Región AWS del bucket |
| `AccessKeyId` | `string?` | `null` | AWS Access Key ID (opcional si se usa IAM Role) |
| `SecretAccessKey` | `string?` | `null` | AWS Secret Access Key |
| `UseIAMRole` | `bool` | `false` | Si es `true`, ignora `AccessKeyId` y `SecretAccessKey` |
| `ServiceUrl` | `string?` | `null` | URL del servicio (para MinIO u otros compatibles S3) |
| `ForcePathStyle` | `bool` | `false` | Fuerza path style en vez de virtual host style. Se activa automáticamente con MinIO |
| `CdnBaseUrl` | `string?` | `null` | URL base del CDN. Si está presente, `GetUrlAsync` retorna la URL del CDN |
| `MultipartThresholdMb` | `int` | `100` | Tamaño en MB a partir del cual se activa la subida multiparte |
| `MultipartChunkSizeMb` | `int` | `8` | Tamaño de cada chunk en la subida multiparte |

---

## Ejemplo de appsettings.json

```json
{
  "ValiBlob:AWS": {
    "Bucket": "mi-empresa-archivos",
    "Region": "sa-east-1",
    "UseIAMRole": true,
    "CdnBaseUrl": "https://d1234567890.cloudfront.net",
    "MultipartThresholdMb": 100,
    "MultipartChunkSizeMb": 8
  }
}
```

---

## Configuración por código

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS(opts =>
    {
        opts.Bucket = "mi-empresa-archivos";
        opts.Region = "sa-east-1";
        opts.UseIAMRole = true;
        opts.CdnBaseUrl = "https://d1234567890.cloudfront.net";
        opts.MultipartThresholdMb = 100;
        opts.MultipartChunkSizeMb = 8;
    })
    .WithDefaultProvider("AWS");
```

---

## Compatibilidad con MinIO (self-hosted)

MinIO es totalmente compatible con la API S3 pero requiere dos configuraciones extra: `ServiceUrl` y `ForcePathStyle`. ValiBlob incluye un método de extensión dedicado para esto.

### Con `UseMinIO`

```csharp
builder.Services
    .AddValiBlob()
    .UseMinIO(opts =>
    {
        opts.Bucket = "mi-bucket";
        opts.ServiceUrl = "http://localhost:9000";
        opts.AccessKeyId = "minioadmin";
        opts.SecretAccessKey = "minioadmin";
        // ForcePathStyle se activa automáticamente por UseMinIO
    })
    .WithDefaultProvider("AWS");
```

### Con `UseAWS` directamente

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS(opts =>
    {
        opts.Bucket = "mi-bucket";
        opts.ServiceUrl = "http://localhost:9000";
        opts.ForcePathStyle = true;
        opts.AccessKeyId = "minioadmin";
        opts.SecretAccessKey = "minioadmin";
    })
    .WithDefaultProvider("AWS");
```

### MinIO con Docker Compose para desarrollo

```yaml
# docker-compose.yml
services:
  minio:
    image: minio/minio:latest
    command: server /data --console-address ":9001"
    ports:
      - "9000:9000"
      - "9001:9001"
    environment:
      MINIO_ROOT_USER: minioadmin
      MINIO_ROOT_PASSWORD: minioadmin
    volumes:
      - minio_data:/data

volumes:
  minio_data:
```

---

## Subida multiparte

Para archivos grandes, AWS S3 soporta Multipart Upload, que divide el archivo en chunks y los sube en paralelo. ValiBlob activa esto automáticamente cuando el archivo supera `MultipartThresholdMb` (default: 100 MB).

**Cuándo se activa:**

- El contenido del `UploadRequest` es mayor a `MultipartThresholdMb`
- O cuando `UploadOptions.UseMultipart = true` explícitamente

```csharp
var request = new UploadRequest
{
    Path = StoragePath.From("videos", "presentacion.mp4"),
    Content = videoStream,
    ContentType = "video/mp4",
    ContentLength = 500_000_000, // 500 MB — activará multiparte
    Options = new UploadOptions
    {
        UseMultipart = true,      // forzar multiparte sin importar el tamaño
        ChunkSizeMb = 16           // tamaño del chunk en MB
    }
};

var progress = new Progress<UploadProgress>(p =>
    Console.WriteLine($"Subiendo: {p.Percentage:F1}%"));

var result = await _storage.UploadAsync(request, progress);
```

---

## URLs prefirmadas

`AWSS3Provider` implementa `IPresignedUrlProvider`. Podés obtener URLs temporales para upload o download sin exponer tus credenciales al cliente.

```csharp
using ValiBlob.Core.Abstractions;

// Inyectá IStorageFactory y casteá a IPresignedUrlProvider
var factory = serviceProvider.GetRequiredService<IStorageFactory>();
var provider = factory.Create("AWS") as IPresignedUrlProvider;

if (provider is not null)
{
    // URL de descarga válida por 1 hora
    var downloadUrl = await provider.GetPresignedDownloadUrlAsync(
        "documentos/contrato.pdf",
        TimeSpan.FromHours(1));

    if (downloadUrl.IsSuccess)
        Console.WriteLine($"URL de descarga: {downloadUrl.Value}");

    // URL de upload válida por 15 minutos
    var uploadUrl = await provider.GetPresignedUploadUrlAsync(
        "documentos/nuevo-contrato.pdf",
        TimeSpan.FromMinutes(15));

    if (uploadUrl.IsSuccess)
        Console.WriteLine($"URL de upload: {uploadUrl.Value}");
}
```

---

## BucketOverride (multi-tenant)

Podés especificar un bucket diferente por operación sin cambiar la configuración global. Esto es ideal para escenarios multi-tenant donde cada cliente tiene su propio bucket.

```csharp
// Subir en el bucket del tenant
var result = await _storage.UploadAsync(new UploadRequest
{
    Path = StoragePath.From("reportes", "ventas-q1.pdf"),
    Content = stream,
    ContentType = "application/pdf",
    BucketOverride = $"tenant-{tenantId}-archivos"
});

// Descargar del bucket del tenant
var download = await _storage.DownloadAsync(new DownloadRequest
{
    Path = StoragePath.From("reportes", "ventas-q1.pdf"),
    BucketOverride = $"tenant-{tenantId}-archivos"
});
```

Ver [Multi-tenant](../multi-tenant.md) para estrategias más avanzadas.

---

## Limitaciones

| Limitación | Detalle |
|---|---|
| Tamaño máximo de objeto | 5 TB (límite de AWS S3) |
| Subida simple máxima | 5 GB (sin multiparte) |
| Multiparte mínimo por chunk | 5 MB (excepto el último) |
| `SetMetadataAsync` | En S3, actualizar metadata requiere una re-copia del objeto (copia a sí mismo con nueva metadata). ValiBlob lo hace transparentemente, pero implica tráfico adicional |
| Nombres de bucket | Entre 3 y 63 caracteres, solo minúsculas, números y guiones |
