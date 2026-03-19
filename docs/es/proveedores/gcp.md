# Proveedor Google Cloud Storage

Este documento cubre la configuración del proveedor `ValiBlob.GCP` para Google Cloud Storage (GCS).

---

## Instalación

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.GCP
```

---

## Autenticación

Google Cloud Storage soporta tres formas de autenticación en ValiBlob, con un orden de prioridad bien definido.

### Opción 1: Application Default Credentials (recomendado para producción)

Cuando tu aplicación corre en Google Cloud (GKE, Cloud Run, Compute Engine, App Engine), las credenciales se obtienen automáticamente del metadata service. No necesitás configurar nada en el código.

```json
{
  "ValiBlob:GCP": {
    "Bucket": "mi-bucket-gcp",
    "ProjectId": "mi-proyecto-gcp"
  }
}
```

```csharp
builder.Services
    .AddValiBlob()
    .UseGCP(opts =>
    {
        opts.Bucket = "mi-bucket-gcp";
        opts.ProjectId = "mi-proyecto-gcp";
        // Sin CredentialsPath ni CredentialsJson → usa ADC automáticamente
    })
    .WithDefaultProvider("GCP");
```

Para desarrollo local con ADC, autenticá con:

```bash
gcloud auth application-default login
```

### Opción 2: Archivo de credenciales (Service Account JSON)

Descargá la clave de la service account desde Google Cloud Console → IAM → Service Accounts → tu cuenta → "Add Key" → JSON.

```json
{
  "ValiBlob:GCP": {
    "Bucket": "mi-bucket-gcp",
    "ProjectId": "mi-proyecto-gcp",
    "CredentialsPath": "/ruta/a/service-account.json"
  }
}
```

```csharp
builder.Services
    .AddValiBlob()
    .UseGCP(opts =>
    {
        opts.Bucket = "mi-bucket-gcp";
        opts.ProjectId = "mi-proyecto-gcp";
        opts.CredentialsPath = "/ruta/al/service-account.json";
    })
    .WithDefaultProvider("GCP");
```

> **💡 Tip:** En Kubernetes, montá el archivo de credenciales como un Secret y pasá la ruta como variable de entorno.

### Opción 3: JSON embebido en configuración

Si preferís no tener un archivo en el filesystem, podés pasar el contenido del JSON directamente. Esto es útil con sistemas de secretos que inyectan valores como strings.

```csharp
builder.Services
    .AddValiBlob()
    .UseGCP(opts =>
    {
        opts.Bucket = "mi-bucket-gcp";
        opts.ProjectId = "mi-proyecto-gcp";
        opts.CredentialsJson = Environment.GetEnvironmentVariable("GCP_CREDENTIALS_JSON");
    })
    .WithDefaultProvider("GCP");
```

> **⚠️ Advertencia:** No incluyas el JSON de credenciales directamente en `appsettings.json` ni lo versiones en el repositorio. Usá variables de entorno, Kubernetes Secrets, o un servicio de secretos como Google Secret Manager.

**Orden de prioridad:** `CredentialsPath` → `CredentialsJson` → Application Default Credentials.

---

## Referencia completa de `GCPStorageOptions`

| Propiedad | Tipo | Default | Descripción |
|---|---|---|---|
| `Bucket` | `string` | `""` | Nombre del bucket GCS por defecto |
| `ProjectId` | `string` | `""` | ID del proyecto GCP |
| `CredentialsPath` | `string?` | `null` | Ruta al archivo JSON de la service account |
| `CredentialsJson` | `string?` | `null` | Contenido JSON de la service account como string |
| `CdnBaseUrl` | `string?` | `null` | URL base del CDN (Cloud CDN o un CDN externo) |

---

## Ejemplo completo de appsettings.json

```json
{
  "ValiBlob:GCP": {
    "Bucket": "mi-empresa-storage",
    "ProjectId": "mi-proyecto-12345",
    "CredentialsPath": "/secrets/gcp-service-account.json",
    "CdnBaseUrl": "https://cdn.midominio.com"
  }
}
```

---

## Configuración de CDN

Si tenés Google Cloud CDN configurado frente a tu bucket, configurá `CdnBaseUrl` para que `GetUrlAsync` retorne URLs del CDN:

```json
{
  "ValiBlob:GCP": {
    "Bucket": "mi-bucket",
    "ProjectId": "mi-proyecto",
    "CdnBaseUrl": "https://storage.googleapis.com/mi-bucket"
  }
}
```

Para buckets públicos, la URL directa de GCS es:
`https://storage.googleapis.com/{bucket}/{path}`

Si configurás Cloud CDN, la URL sería la de tu load balancer o dominio personalizado.

---

## Permisos de la Service Account

La service account usada por ValiBlob necesita los siguientes permisos en el bucket:

| Permiso IAM | Operación |
|---|---|
| `storage.objects.create` | Upload |
| `storage.objects.get` | Download, GetMetadata, GetUrl |
| `storage.objects.delete` | Delete |
| `storage.objects.list` | ListFiles, ListFolders, ListAll |
| `storage.objects.update` | SetMetadata, Copy, Move |

El rol predefinido `roles/storage.objectAdmin` incluye todos estos permisos.

---

## BucketOverride

Podés apuntar a un bucket diferente por operación, útil para separación multi-tenant o para mover archivos entre buckets:

```csharp
// Subir en el bucket del tenant
var result = await _storage.UploadAsync(new UploadRequest
{
    Path = StoragePath.From("reportes", "ventas.pdf"),
    Content = stream,
    ContentType = "application/pdf",
    BucketOverride = $"tenant-{tenantId}-data"
});

// Copiar entre buckets
await _storage.CopyAsync(
    sourcePath: StoragePath.From("origen", "archivo.pdf"),
    destinationPath: StoragePath.From("destino", "archivo.pdf")
    // BucketOverride no aplica a Copy — la copia ocurre dentro del mismo bucket configurado
);
```

---

## Limitaciones

| Limitación | Detalle |
|---|---|
| Tamaño máximo de objeto | 5 TB |
| Nombres de bucket | 3-63 caracteres, globalmente únicos en todo GCS |
| `SetMetadataAsync` | GCS permite actualizar metadata directamente sin re-subir el objeto |
| URLs firmadas | GCS también soporta URLs firmadas (Signed URLs). ValiBlob expone esto via `IPresignedUrlProvider` cuando el proveedor lo implementa |
| Regiones | Los buckets de GCS son regionales, multi-regionales o dual-regionales. Elegí según tu latencia y requisitos de disponibilidad |
| Acceso público | Para buckets públicos, asegurate de no configurar IAM de bucket que bloquee el acceso público |
