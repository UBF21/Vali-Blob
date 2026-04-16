# ValiBlob

> Abstracción de almacenamiento cloud para .NET — un solo contrato, múltiples proveedores.

ValiBlob es una librería .NET que unifica el acceso a los principales servicios de almacenamiento de objetos (AWS S3, Azure Blob Storage, Google Cloud Storage, Oracle OCI, Supabase Storage) detrás de una única interfaz. Permite cambiar de proveedor cloud sin modificar tu lógica de negocio.

---

## ¿Por qué ValiBlob?

El problema del **vendor lock-in** en almacenamiento es sutil pero costoso: cuando escribís código directamente contra el SDK de AWS S3, cada llamada a `PutObjectAsync`, cada manejo de `AmazonS3Exception` y cada construcción de URL queda acoplada a ese proveedor. Migrar a Azure o GCP implica reescribir todo ese código.

ValiBlob resuelve esto con un único contrato (`IStorageProvider`) que todos los proveedores implementan. Tu código de aplicación nunca importa un SDK de cloud — sólo depende de `ValiBlob.Core`.

**Beneficios adicionales:**

- Pipeline de middleware para validación, compresión y cifrado, igual que ASP.NET Core
- Resiliencia automática con reintentos, circuit breaker y timeout (vía Polly)
- Telemetría OpenTelemetry lista para usar
- Event hooks para auditoría y monitoreo
- Provider InMemory para tests sin credenciales cloud
- Health Checks para integración con `/healthz`

---

## Características

| Característica | Descripción |
|---|---|
| Interfaz unificada | `IStorageProvider` para todos los proveedores |
| Multi-proveedor | Registrá N proveedores simultáneamente |
| Pipeline de middleware | Validación, compresión, cifrado AES-256 |
| Resiliencia | Reintentos, circuit breaker, timeout (Polly) |
| Progress reporting | `IProgress<UploadProgress>` con porcentaje |
| Operaciones batch | `DeleteManyAsync`, `ListAllAsync` (streaming) |
| Operaciones de carpeta | `DeleteFolderAsync`, `ListFoldersAsync` |
| **Subida reanudable** | `IResumableUploadProvider` — subidas en chunks con pausa y reanudación; AWS, Azure, Supabase (TUS), GCP, OCI |
| Remote upload | `UploadFromUrlAsync` sin pasar por el servidor |
| URLs prefirmadas | Upload y download via `IPresignedUrlProvider` |
| Download por rango | Soporte para byte range requests |
| Multi-tenant | `BucketOverride` por request |
| Telemetría | Métricas y actividades OpenTelemetry |
| Event hooks | `IStorageEventHandler` para auditoría |
| Health Checks | Integración con ASP.NET Core |
| Sistema de archivos local | `ValiBlob.Local` — proveedor en disco para desarrollo y Docker Compose |
| Testing | `InMemoryStorageProvider` |

---

## Proveedores soportados

| Proveedor | Package NuGet | Clave |
|---|---|---|
| Amazon S3 | `ValiBlob.AWS` | `"AWS"` |
| MinIO (self-hosted) | `ValiBlob.AWS` | `"AWS"` |
| Azure Blob Storage | `ValiBlob.Azure` | `"Azure"` |
| Google Cloud Storage | `ValiBlob.GCP` | `"GCP"` |
| Oracle OCI Object Storage | `ValiBlob.OCI` | `"OCI"` |
| Supabase Storage | `ValiBlob.Supabase` | `"Supabase"` |
| Sistema de archivos local | `ValiBlob.Local` | `"Local"` |
| InMemory (testing) | `ValiBlob.Testing` | `"InMemory"` |

---

## Instalación

Instalá sólo los packages que necesitás:

```bash
# Core (obligatorio)
dotnet add package ValiBlob.Core

# Proveedores (elegí los que uses)
dotnet add package ValiBlob.AWS
dotnet add package ValiBlob.Azure
dotnet add package ValiBlob.GCP
dotnet add package ValiBlob.OCI
dotnet add package ValiBlob.Supabase
dotnet add package ValiBlob.Local

# Extras
dotnet add package ValiBlob.HealthChecks
dotnet add package ValiBlob.Testing
```

---

## Inicio rápido (5 minutos)

### 1. Instalar el package

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.AWS
```

### 2. Configurar en `appsettings.json`

```json
{
  "ValiBlob": {
    "DefaultProvider": "AWS"
  },
  "ValiBlob:AWS": {
    "Bucket": "mi-bucket",
    "Region": "us-east-1",
    "AccessKeyId": "AKIAIOSFODNN7EXAMPLE",
    "SecretAccessKey": "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
  }
}
```

### 3. Registrar servicios en `Program.cs`

```csharp
using ValiBlob.Core.DependencyInjection;
using ValiBlob.AWS.Extensions;

builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithDefaultProvider("AWS");
```

### 4. Usar `IStorageProvider`

```csharp
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;

public class DocumentService
{
    private readonly IStorageProvider _storage;

    public DocumentService(IStorageFactory factory)
    {
        _storage = factory.Create(); // usa el proveedor por defecto
    }

    public async Task SubirDocumentoAsync(Stream contenido, string nombre)
    {
        var request = new UploadRequest
        {
            Path = StoragePath.From("documentos", nombre),
            Content = contenido,
            ContentType = "application/pdf"
        };

        var result = await _storage.UploadAsync(request);

        if (!result.IsSuccess)
            throw new Exception($"Error al subir: {result.ErrorMessage}");

        Console.WriteLine($"Subido: {result.Value!.Path} ({result.Value.SizeBytes:N0} bytes)");
    }

    public async Task<Stream> DescargarDocumentoAsync(string nombre)
    {
        var result = await _storage.DownloadAsync(new DownloadRequest
        {
            Path = StoragePath.From("documentos", nombre)
        });

        if (!result.IsSuccess)
            throw new Exception($"Error al descargar: {result.ErrorMessage}");

        return result.Value!;
    }
}
```

---

## Diagrama de arquitectura

```
┌──────────────────────────────────────────────────────────────────┐
│                        Tu aplicación                             │
│                                                                  │
│   DocumentService   OrderService   ReportService                 │
│         │                │               │                       │
│         └────────────────┴───────────────┘                       │
│                          │                                       │
│               IStorageProvider / IStorageFactory                 │
└──────────────────────────┬───────────────────────────────────────┘
                           │
┌──────────────────────────▼───────────────────────────────────────┐
│                      ValiBlob.Core                               │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐     │
│  │                   Pipeline de Middleware                 │     │
│  │   ValidationMiddleware → CompressionMiddleware →         │     │
│  │   EncryptionMiddleware → [tus middlewares]               │     │
│  └─────────────────────────────┬───────────────────────────┘     │
│                                │                                 │
│  Resiliencia (Polly)           │    Event Hooks                  │
│  Telemetría (OTel)            │    Health Checks                 │
└────────────────────────────────┼─────────────────────────────────┘
                                 │
         ┌───────────────────────┼───────────────────────┐
         │               │               │               │
   ┌─────▼─────┐   ┌─────▼─────┐  ┌─────▼────┐  ┌──────▼────┐
   │ ValiBlob  │   │ ValiBlob  │  │ ValiBlob │  │ ValiBlob  │
   │   .AWS    │   │  .Azure   │  │  .GCP    │  │   .OCI    │
   └─────┬─────┘   └─────┬─────┘  └─────┬────┘  └──────┬────┘
         │               │               │               │
      AWS S3          Azure Blob       GCS           OCI OS
      MinIO           Storage         Storage        Storage
```

---

## Documentación completa

| Documento | Descripción |
|---|---|
| [Primeros pasos](primeros-pasos.md) | Instalación, configuración y primer uso |
| [StoragePath](storage-path.md) | Rutas tipadas para evitar errores de strings |
| [Pipeline](pipeline.md) | Middleware de validación, compresión y cifrado |
| [Detección de tipo de contenido](deteccion-tipo-contenido.md) | Inspección de magic bytes para detectar tipos MIME reales |
| [Deduplicación](deduplicacion.md) | Hash SHA-256 y detección de contenido duplicado |
| [Escaneo de virus](escaneo-virus.md) | Análisis antivirus conectable vía `IVirusScanner` |
| [Cuotas de almacenamiento](cuotas.md) | Límites de uso por alcance y aplicación de cuotas |
| [Resolución de conflictos](resolucion-conflictos.md) | Sobreescribir, renombrar o fallar ante conflictos de ruta |
| [Resiliencia](resiliencia.md) | Reintentos, circuit breaker y timeout |
| [Eventos](eventos.md) | Event hooks para auditoría |
| [Telemetría](telemetria.md) | OpenTelemetry, métricas y trazas |
| [Health Checks](health-checks.md) | Monitoreo de salud del storage |
| [Multi-tenant](multi-tenant.md) | BucketOverride y estrategias de aislamiento |
| [Operaciones batch](operaciones-batch.md) | Batch delete, list streaming, upload desde URL |
| [Subida reanudable](subida-reanudable.md) | Subidas en chunks, protocolo TUS, session store, matriz de proveedores |
| [Almacenes de sesión](almacenes-sesion.md) | Stores Redis y EF Core para subidas reanudables |
| [Migración de storage](migracion.md) | Migración de archivos entre proveedores con dry run y progreso |
| [Integración con CDN](cdn.md) | Mapear rutas a URLs de CDN e invalidar caché |
| [Procesamiento de imágenes](procesamiento-imagenes.md) | Redimensionar, reformatear y generar miniaturas con ImageSharp |
| [Helpers de ruta](helpers-ruta.md) | Prefijos de fecha, sufijos de hash, sufijos aleatorios, saneamiento |
| [Testing](testing.md) | InMemoryStorageProvider y tests sin cloud |
| [Seguridad](seguridad.md) | Path traversal, credenciales, cifrado, mínimo privilegio |
| [Cifrado y descifrado](cifrado-descifrado.md) | Ciclo completo AES-256-CBC, gestión de claves, cifrado + compresión |
| [Solución de problemas](solucion-problemas.md) | Errores comunes y sus correcciones |
| [Referencia API](referencia-api.md) | Documentación completa de la API |
| **Proveedores** | |
| [AWS S3 / MinIO](proveedores/aws.md) | Amazon S3 y MinIO self-hosted |
| [Azure Blob Storage](proveedores/azure.md) | Microsoft Azure |
| [Google Cloud Storage](proveedores/gcp.md) | Google Cloud Platform |
| [Oracle OCI](proveedores/oci.md) | Oracle Cloud Infrastructure |
| [Supabase Storage](proveedores/supabase.md) | Supabase |
| [Sistema de archivos local](proveedores/local.md) | Proveedor local para desarrollo y testing |

---

## Compatibilidad

| Framework | Versión mínima |
|---|---|
| .NET Standard | 2.0+ |
| .NET | 6.0+ |
| .NET | 7.0+ |
| .NET | 8.0+ |
| .NET | 9.0+ |
| ASP.NET Core | 6.0+ |

---

## Licencia

ValiBlob se distribuye bajo licencia MIT. Ver el archivo `LICENSE` en la raíz del repositorio.
