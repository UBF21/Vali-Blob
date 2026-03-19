# Primeros pasos con ValiBlob

Esta guía te lleva desde cero hasta tener tu primera operación de almacenamiento funcionando.

---

## Prerrequisitos

- .NET 8.0 SDK o superior
- Una cuenta en al menos uno de los proveedores soportados (o Docker para MinIO local)
- Conocimientos básicos de inyección de dependencias en ASP.NET Core

---

## Instalación

### Core (obligatorio)

```bash
dotnet add package ValiBlob.Core
```

### Proveedores (instalá los que vayas a usar)

```bash
# Amazon S3 y MinIO
dotnet add package ValiBlob.AWS

# Azure Blob Storage
dotnet add package ValiBlob.Azure

# Google Cloud Storage
dotnet add package ValiBlob.GCP

# Oracle OCI Object Storage
dotnet add package ValiBlob.OCI

# Supabase Storage
dotnet add package ValiBlob.Supabase
```

### Extras

```bash
# Health checks para /healthz
dotnet add package ValiBlob.HealthChecks

# InMemory provider para tests
dotnet add package ValiBlob.Testing
```

---

## Configuración básica

### Program.cs (API mínima / .NET 8+)

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.AWS.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithDefaultProvider("AWS");

var app = builder.Build();
app.Run();
```

### Startup.cs (estilo clásico)

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.AWS.Extensions;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddValiBlob()
            .UseAWS()
            .WithDefaultProvider("AWS");
    }
}
```

### Múltiples proveedores simultáneos

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .UseAzure()
    .UseGCP()
    .WithDefaultProvider("AWS");
```

---

## appsettings.json completo

El siguiente ejemplo muestra la configuración de todos los proveedores y opciones disponibles. En producción sólo incluirás las secciones que uses.

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
      "MaxFileSizeBytes": 524288000,
      "AllowedExtensions": [".pdf", ".png", ".jpg", ".docx"],
      "BlockedExtensions": [".exe", ".bat", ".cmd", ".sh"],
      "AllowedContentTypes": ["application/pdf", "image/png", "image/jpeg"]
    },
    "Compression": {
      "Enabled": true,
      "MinSizeBytes": 1024,
      "CompressibleContentTypes": [
        "text/plain",
        "text/html",
        "application/json",
        "application/xml"
      ]
    }
  },
  "ValiBlob:AWS": {
    "Bucket": "mi-bucket-produccion",
    "Region": "us-east-1",
    "AccessKeyId": "AKIAIOSFODNN7EXAMPLE",
    "SecretAccessKey": "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
    "UseIAMRole": false,
    "CdnBaseUrl": "https://cdn.midominio.com",
    "MultipartThresholdMb": 100,
    "MultipartChunkSizeMb": 8
  },
  "ValiBlob:Azure": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=miCuenta;AccountKey=miClave;EndpointSuffix=core.windows.net",
    "Container": "mi-contenedor",
    "CdnBaseUrl": "https://cdn.midominio.com",
    "CreateContainerIfNotExists": true
  },
  "ValiBlob:GCP": {
    "Bucket": "mi-bucket-gcp",
    "ProjectId": "mi-proyecto-gcp",
    "CredentialsPath": "/ruta/a/service-account.json",
    "CdnBaseUrl": "https://cdn.midominio.com"
  },
  "ValiBlob:OCI": {
    "Namespace": "mi-namespace-oci",
    "Bucket": "mi-bucket-oci",
    "Region": "sa-saopaulo-1",
    "TenancyId": "ocid1.tenancy.oc1..aaaaaa",
    "UserId": "ocid1.user.oc1..aaaaaa",
    "Fingerprint": "20:3b:97:13:55:1c",
    "PrivateKeyPath": "/ruta/a/oci_api_key.pem"
  },
  "ValiBlob:Supabase": {
    "Url": "https://xyzcompany.supabase.co",
    "ApiKey": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "Bucket": "mi-bucket-supabase"
  }
}
```

> **⚠️ Advertencia:** Nunca comitas credenciales reales en el repositorio. Usá `dotnet user-secrets` para desarrollo local y variables de entorno o un servicio de secretos (AWS Secrets Manager, Azure Key Vault) para producción.

---

## Primer upload

```csharp
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;

public class EjemploSubida
{
    private readonly IStorageProvider _storage;

    public EjemploSubida(IStorageFactory factory)
    {
        _storage = factory.Create(); // proveedor por defecto
    }

    public async Task SubirArchivoAsync(string rutaLocal, string nombreDestino)
    {
        await using var fileStream = File.OpenRead(rutaLocal);

        var request = new UploadRequest
        {
            Path = StoragePath.From("documentos", nombreDestino),
            Content = fileStream,
            ContentType = "application/pdf",
            ContentLength = new FileInfo(rutaLocal).Length,
            Metadata = new Dictionary<string, string>
            {
                { "autor", "Felipe" },
                { "proyecto", "facturacion" }
            }
        };

        // Subida con reporte de progreso
        var progress = new Progress<UploadProgress>(p =>
        {
            if (p.Percentage.HasValue)
                Console.WriteLine($"Progreso: {p.Percentage:F1}%");
        });

        var result = await _storage.UploadAsync(request, progress);

        if (result.IsSuccess)
        {
            Console.WriteLine($"Archivo subido exitosamente:");
            Console.WriteLine($"  Ruta: {result.Value!.Path}");
            Console.WriteLine($"  Tamaño: {result.Value.SizeBytes:N0} bytes");
            Console.WriteLine($"  ETag: {result.Value.ETag}");
        }
        else
        {
            Console.WriteLine($"Error [{result.ErrorCode}]: {result.ErrorMessage}");
        }
    }
}
```

---

## Primera descarga

```csharp
public async Task DescargarArchivoAsync(string path, string rutaDestino)
{
    var result = await _storage.DownloadAsync(new DownloadRequest
    {
        Path = StoragePath.From(path)
    });

    if (!result.IsSuccess)
    {
        Console.WriteLine($"Error al descargar [{result.ErrorCode}]: {result.ErrorMessage}");
        return;
    }

    await using var fileStream = File.Create(rutaDestino);
    await result.Value!.CopyToAsync(fileStream);
    Console.WriteLine($"Archivo descargado en: {rutaDestino}");
}

// Descarga parcial (byte range)
public async Task DescargarRangoAsync(string path, long desde, long hasta)
{
    var result = await _storage.DownloadAsync(new DownloadRequest
    {
        Path = StoragePath.From(path),
        Range = new DownloadRange { From = desde, To = hasta }
    });

    if (result.IsSuccess)
    {
        // result.Value es un Stream con sólo los bytes del rango solicitado
        using var ms = new MemoryStream();
        await result.Value!.CopyToAsync(ms);
        Console.WriteLine($"Bytes recibidos: {ms.Length}");
    }
}
```

---

## Primer delete

```csharp
public async Task EliminarArchivoAsync(string path)
{
    // StorageResult (sin genérico) — sólo éxito o falla
    var result = await _storage.DeleteAsync(StoragePath.From(path));

    if (result.IsSuccess)
        Console.WriteLine("Archivo eliminado.");
    else
        Console.WriteLine($"No se pudo eliminar [{result.ErrorCode}]: {result.ErrorMessage}");
}

// Verificar existencia antes de eliminar
public async Task EliminarSiExisteAsync(string path)
{
    var exists = await _storage.ExistsAsync(path);

    if (exists.IsSuccess && exists.Value)
    {
        await _storage.DeleteAsync(path);
        Console.WriteLine("Eliminado.");
    }
    else
    {
        Console.WriteLine("El archivo no existe.");
    }
}
```

---

## Errores comunes y soluciones

### `InvalidOperationException: No default provider configured`

**Causa:** No llamaste a `WithDefaultProvider()` o la propiedad `DefaultProvider` en `appsettings.json` está vacía.

**Solución:**
```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithDefaultProvider("AWS"); // este paso es obligatorio
```

### `InvalidOperationException: ValiBlob Azure: provide either ConnectionString or AccountName + AccountKey`

**Causa:** Configuraste el proveedor Azure pero no proviste credenciales.

**Solución:** Agregá la sección `ValiBlob:Azure` en `appsettings.json` con `ConnectionString` o el par `AccountName` + `AccountKey`.

### El resultado siempre es `StorageErrorCode.FileNotFound`

**Causa:** La ruta del archivo no coincide con la usada al subir. El path es case-sensitive en todos los proveedores.

**Solución:** Usá `StoragePath.From(...)` de forma consistente. `StoragePath.From("Docs", "file.pdf")` y `StoragePath.From("docs", "file.pdf")` son rutas diferentes.

### `StorageErrorCode.ValidationFailed` al subir

**Causa:** El pipeline de validación está activo y el archivo no cumple las reglas (extensión bloqueada, tamaño excedido, tipo de contenido no permitido).

**Solución:** Revisá la sección `ValiBlob:Validation` en tu configuración. Consultá [Pipeline](pipeline.md) para detalles.

### Los reintentos no funcionan

**Causa:** No registraste las políticas de resiliencia, o el paquete de Polly no está instalado como dependencia transitiva.

**Solución:**
```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithResiliencePolicies(opt =>
    {
        opt.RetryCount = 3;
        opt.UseExponentialBackoff = true;
    });
```

### Los tests fallan porque no hay credenciales cloud

**Causa:** Estás usando el proveedor real en tests.

**Solución:** Usá `ValiBlob.Testing` con el proveedor InMemory. Ver [Testing](testing.md).
