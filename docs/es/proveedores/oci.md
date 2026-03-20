# Proveedor Oracle OCI Object Storage

Este documento cubre la configuración del proveedor `ValiBlob.OCI` para Oracle Cloud Infrastructure Object Storage.

---

## Instalación

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.OCI
```

---

## Autenticación

OCI usa un esquema de autenticación basado en API Key con firma de requests mediante RSA. Hay dos maneras de configurarlo en ValiBlob.

### Opción 1: Parámetros explícitos en configuración

Provés todos los datos de la API Key directamente en la configuración. Este es el enfoque más explícito y funciona bien en ambientes sin archivos de configuración OCI.

```json
{
  "ValiBlob:OCI": {
    "Namespace": "mynamespace123",
    "Bucket": "mi-bucket-oci",
    "Region": "sa-saopaulo-1",
    "TenancyId": "ocid1.tenancy.oc1..aaaaaaaXXXX",
    "UserId": "ocid1.user.oc1..aaaaaaaXXXX",
    "Fingerprint": "20:3b:97:13:55:1c:5b:0d:d3:37:d8:50:4e:c5:3a:34",
    "PrivateKeyPath": "/ruta/a/oci_api_key.pem"
  }
}
```

```csharp
builder.Services
    .AddValiBlob()
    .UseOCI(opts =>
    {
        opts.Namespace = "mynamespace123";
        opts.Bucket = "mi-bucket-oci";
        opts.Region = "sa-saopaulo-1";
        opts.TenancyId = "ocid1.tenancy.oc1..aaaaaaaXXXX";
        opts.UserId = "ocid1.user.oc1..aaaaaaaXXXX";
        opts.Fingerprint = "20:3b:97:13:55:1c:5b:0d:d3:37:d8:50:4e:c5:3a:34";
        opts.PrivateKeyPath = "/ruta/a/oci_api_key.pem";
    })
    .WithDefaultProvider("OCI");
```

### Opción 2: Archivo de configuración OCI (desarrollo local)

Si tenés el CLI de OCI configurado localmente, el proveedor puede leer el perfil `DEFAULT` del archivo `~/.oci/config` automáticamente. Esto ocurre cuando no se proveen `TenancyId`, `UserId`, `Fingerprint` y `PrivateKeyPath`.

```json
{
  "ValiBlob:OCI": {
    "Namespace": "mynamespace123",
    "Bucket": "mi-bucket-oci",
    "Region": "sa-saopaulo-1"
  }
}
```

En este caso, el SDK de OCI lee el perfil `DEFAULT` del archivo `~/.oci/config`:

```ini
# ~/.oci/config
[DEFAULT]
user=ocid1.user.oc1..aaaaaaaXXXX
fingerprint=20:3b:97:13:55:1c:5b:0d:d3:37:d8:50:4e:c5:3a:34
tenancy=ocid1.tenancy.oc1..aaaaaaaXXXX
region=sa-saopaulo-1
key_file=~/.oci/oci_api_key.pem
```

> **💡 Tip:** Para generar una API Key de OCI: ve a la Consola OCI → tu perfil de usuario → "API Keys" → "Add API Key".

### Opción 3: Clave privada como string en configuración

Si tu sistema de secretos inyecta la clave como string (no como archivo), podés usar `PrivateKeyContent`:

```csharp
builder.Services
    .AddValiBlob()
    .UseOCI(opts =>
    {
        opts.Namespace = "mynamespace123";
        opts.Bucket = "mi-bucket-oci";
        opts.Region = "sa-saopaulo-1";
        opts.TenancyId = "ocid1.tenancy.oc1..aaaaaaaXXXX";
        opts.UserId = "ocid1.user.oc1..aaaaaaaXXXX";
        opts.Fingerprint = "20:3b:97:13:55:1c:5b:0d:d3:37:d8:50:4e:c5:3a:34";
        opts.PrivateKeyContent = Environment.GetEnvironmentVariable("OCI_PRIVATE_KEY_PEM");
    })
    .WithDefaultProvider("OCI");
```

---

## Referencia completa de `OCIStorageOptions`

| Propiedad | Tipo | Default | Descripción |
|---|---|---|---|
| `Namespace` | `string` | `""` | Namespace del Object Storage en OCI (se obtiene en la Consola OCI) |
| `Bucket` | `string` | `""` | Nombre del bucket por defecto |
| `Region` | `string` | `"sa-saopaulo-1"` | Región OCI donde se encuentra el bucket |
| `TenancyId` | `string?` | `null` | OCID de la tenancy. Requerido para autenticación explícita |
| `UserId` | `string?` | `null` | OCID del usuario. Requerido para autenticación explícita |
| `Fingerprint` | `string?` | `null` | Fingerprint de la API Key |
| `PrivateKeyPath` | `string?` | `null` | Ruta al archivo PEM de la clave privada |
| `PrivateKeyContent` | `string?` | `null` | Contenido PEM de la clave privada como string |
| `CdnBaseUrl` | `string?` | `null` | URL base del CDN para `GetUrlAsync` |

---

## Ejemplo de appsettings.json

```json
{
  "ValiBlob:OCI": {
    "Namespace": "axyz1234namespace",
    "Bucket": "mi-empresa-archivos",
    "Region": "sa-saopaulo-1",
    "TenancyId": "ocid1.tenancy.oc1..aaaaaaaabcdef",
    "UserId": "ocid1.user.oc1..aaaaaaaabcdef",
    "Fingerprint": "20:3b:97:13:55:1c:5b:0d:d3:37:d8:50:4e:c5:3a:34",
    "PrivateKeyPath": "/secrets/oci_api_key.pem",
    "CdnBaseUrl": "https://cdn.midominio.com"
  }
}
```

---

## Regiones OCI comunes

| Región | ID |
|---|---|
| Brasil (São Paulo) | `sa-saopaulo-1` |
| Chile (Santiago) | `sa-santiago-1` |
| Colombia (Bogotá) | `sa-bogota-1` |
| México (Querétaro) | `mx-queretaro-1` |
| EE.UU. (Ashburn) | `us-ashburn-1` |
| EE.UU. (Phoenix) | `us-phoenix-1` |
| Europa (Fráncfort) | `eu-frankfurt-1` |

---

## Limitación de `SetMetadata`

OCI Object Storage no soporta actualizar metadata de un objeto existente in-place. `SetMetadataAsync` en el proveedor OCI **retorna `NotSupported` directamente** — no descarga ni re-sube el objeto automáticamente.

```csharp
var result = await _storage.SetMetadataAsync("documentos/contrato.pdf", metadata);

if (!result.IsSuccess && result.ErrorCode == StorageErrorCode.NotSupported)
{
    // OCI no soporta actualización de metadata in-place.
    // Para cambiar metadata debés re-subir el objeto con los nuevos valores.
}
```

Para actualizar metadata en OCI, re-subí el objeto incluyendo la metadata deseada en `UploadRequest.Metadata`. Si tu caso de uso requiere actualizaciones frecuentes de metadata, considerá usar Azure Blob Storage o GCP, que sí soportan actualizaciones in-place.

---

## BucketOverride

```csharp
// Acceder a un bucket diferente por operación
var result = await _storage.UploadAsync(new UploadRequest
{
    Path = StoragePath.From("backups", "db-dump.sql.gz"),
    Content = stream,
    ContentType = "application/gzip",
    BucketOverride = "backups-bucket"
});
```

---

## URLs prefirmadas (Pre-Authenticated Requests)

`OCIStorageProvider` implementa `IPresignedUrlProvider` usando OCI **Pre-Authenticated Requests (PARs)**.

### Diferencia con AWS/GCP

| | AWS S3 / GCP Cloud Storage | OCI Object Storage |
|---|---|---|
| **Cómo se genera la URL** | Se firma localmente en memoria con tus credenciales — **sin llamada de red** | Se crea un objeto PAR en los servidores de OCI via API — requiere un round-trip |
| **Costo de red por URL** | Cero — operación puramente en CPU | Una llamada HTTP a OCI por cada URL generada |
| **Latencia** | Sub-milisegundo | Depende de la latencia de red hacia OCI (~50–200 ms) |
| **Ciclo de vida server-side** | Sin estado — el cloud no registra la URL | El PAR es un objeto real: puede listarse, desactivarse o eliminarse desde la Consola o CLI |
| **Rate limits** | Ninguno para generación de URLs | OCI aplica rate limits a la creación de PARs |

En cargas de trabajo de baja o moderada frecuencia, el round-trip adicional es despreciable. Para generación de URLs de alta frecuencia, ver la nota de caché más abajo.

### Uso

```csharp
var provider = factory.Create("oci");

if (provider is IPresignedUrlProvider presigned)
{
    // Crea un PAR en OCI con acceso PUT por 15 minutos
    var uploadUrl = await presigned.GetPresignedUploadUrlAsync(
        StoragePath.From("uploads", userId, "reporte.pdf"),
        expiresIn: TimeSpan.FromMinutes(15));

    // Crea un PAR en OCI con acceso GET por 2 horas
    var downloadUrl = await presigned.GetPresignedDownloadUrlAsync(
        "privado/reporte.pdf",
        expiresIn: TimeSpan.FromHours(2));
}
```

### Caché de PARs

Dado que cada generación de URL hace una llamada HTTP a OCI, cacheá la URL cuando el mismo usuario accede al mismo recurso repetidamente dentro de la ventana de validez. Siempre incluí el usuario en la clave de caché — un PAR otorga acceso sin autenticación durante toda su vida útil, por lo que compartirlo entre usuarios distintos es un riesgo de seguridad.

```csharp
var cacheKey = $"oci-par:{userId}:{path}";
if (!cache.TryGetValue(cacheKey, out string? url))
{
    var expiration = TimeSpan.FromHours(2);
    var result = await presigned.GetPresignedDownloadUrlAsync(path, expiration);
    url = result.Value;
    cache.Set(cacheKey, url, expiration * 0.9);
}
```

### Ciclo de vida de PARs

Los PARs son objetos reales en el servidor de OCI. Podés verlos, desactivarlos o eliminarlos en **Storage → Buckets → \<bucket\> → Pre-Authenticated Requests** en la Consola OCI, o vía CLI:

```bash
oci os preauth-request list --bucket-name mi-bucket --namespace mi-namespace
```

Esto permite revocar el acceso a una URL después de haberla emitido — algo que no es posible con AWS ni GCP.

---

## Limitaciones

| Limitación | Detalle |
|---|---|
| `SetMetadataAsync` | Retorna `NotSupported` — re-subí el objeto con la nueva metadata |
| Tamaño máximo de objeto | 10 TB |
| PARs (URLs prefirmadas) | Requieren una llamada HTTP a OCI por cada URL generada (no hay firma local) |
| Namespace | Es único por tenancy y no puede cambiarse |
| Visibilidad de buckets | Los buckets pueden ser públicos o privados. La visibilidad se configura en la Consola OCI, no en ValiBlob |
