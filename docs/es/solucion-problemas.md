# Solución de problemas

Correcciones concretas para los problemas más comunes al ejecutar ValiBlob en producción.

---

## "Upload session not found or expired"

**Síntoma:** `UploadChunkAsync` o `CompleteUploadAsync` devuelve un resultado fallido con el mensaje `Upload session not found or expired`.

**Causa:** Una de las siguientes:

- El proceso de la aplicación fue reiniciado y se perdió el session store en memoria.
- La sesión superó la ventana de `SessionExpiration` configurada antes de que la subida se completara.
- El load balancer está enrutando los requests de chunk a una instancia diferente a la que creó la sesión.

**Solución:**

Configurá un session store persistente respaldado por Redis para que las sesiones sobrevivan a los reinicios y sean compartidas entre instancias:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithResumableUploads(r => r
        .UseRedisSessionStore(opts =>
        {
            opts.ConnectionString = Environment.GetEnvironmentVariable("REDIS_URL");
            opts.KeyPrefix        = "valiblob:sessions:";
        })
        .SetSessionExpiration(TimeSpan.FromHours(24)));
```

Si no podés usar Redis, configurá sticky sessions en tu load balancer para que todos los requests de una subida dada sean enrutados a la misma instancia.

Para aumentar la ventana de sesión sin cambiar infraestructura:

```csharp
.SetSessionExpiration(TimeSpan.FromHours(48))
```

---

## GCP: las URLs prefirmadas devuelven `NotSupported`

**Síntoma:** Llamar a `GetPresignedUploadUrlAsync` o `GetPresignedDownloadUrlAsync` en el proveedor GCP lanza `NotSupportedException` o devuelve un resultado fallido con `NotSupported`.

**Causa:** La generación de URLs prefirmadas de GCP requiere la clave privada de una cuenta de servicio. Cuando la aplicación corre con Application Default Credentials (ADC) — como el metadata server de Compute Engine o `gcloud auth application-default login` — la operación de firma no está disponible.

**Solución:** Proporcioná un archivo de credenciales de cuenta de servicio explícito:

```json
{
  "ValiBlob:GCP": {
    "Bucket": "my-bucket",
    "CredentialsPath": "/run/secrets/gcp-service-account.json"
  }
}
```

O pasá el contenido JSON directamente:

```json
{
  "ValiBlob:GCP": {
    "Bucket": "my-bucket",
    "CredentialsJson": "{ \"type\": \"service_account\", ... }"
  }
}
```

La cuenta de servicio necesita `roles/storage.objectAdmin` en el bucket y `roles/iam.serviceAccountTokenCreator` sobre sí misma. Ver [Seguridad](seguridad.md) para la configuración completa de IAM.

---

## AWS: `InvalidPart` / ETag mismatch en `CompleteMultipartUpload`

**Síntoma:** La llamada final a `CompleteUploadAsync` falla con un error `InvalidPart` o un mismatch de ETag.

**Causa:** La subida multipart de S3 requiere que las partes se listen en el orden exacto en que fueron subidas, emparejadas con el ETag que S3 devolvió para cada parte. Esto puede fallar cuando:

- Las partes fueron subidas en orden incorrecto.
- La sesión se perdió y se inició una sesión nueva, dejando huérfana la subida multipart original de S3.
- El ETag no fue almacenado correctamente entre chunks.

**Solución:**

ValiBlob almacena los ETags de las partes en la sesión. Si una sesión se pierde, la subida multipart de S3 en progreso quedará huérfana (y acumulará costos de almacenamiento hasta que expire). Abortá las subidas huérfanas con:

```bash
aws s3api list-multipart-uploads --bucket my-bucket
aws s3api abort-multipart-upload --bucket my-bucket \
  --key "path/to/file" --upload-id "UPLOAD_ID"
```

Para evitar la acumulación, configurá una regla de lifecycle de S3 para abortar subidas multipart incompletas después de 7 días:

```json
{
  "Rules": [{
    "ID": "abort-incomplete-mpu",
    "Status": "Enabled",
    "AbortIncompleteMultipartUpload": { "DaysAfterInitiation": 7 }
  }]
}
```

---

## El circuit breaker se abre inesperadamente

**Síntoma:** Los requests empiezan a fallar inmediatamente con un error de circuit breaker abierto, aunque el proveedor de almacenamiento es alcanzable.

**Causa:** El circuit breaker se abrió porque se alcanzaron los umbrales de ratio de fallos o throughput mínimo. Esto puede suceder si los umbrales por defecto son demasiado sensibles para tu patrón de tráfico — por ejemplo, una operación bulk que produce varios 404s durante una operación de listado.

**Solución:**

Inspeccioná los logs para saber la razón por la que se abrió el circuito (buscá eventos `CircuitBreakerOpened` en tu telemetría). Luego ajustá los umbrales:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithResilience(r => r
        .CircuitBreaker(cb =>
        {
            cb.FailureRatio       = 0.5;   // abrir cuando el 50% de las llamadas fallen (default: 0.3)
            cb.MinimumThroughput  = 20;    // requerir al menos 20 llamadas en la ventana de muestreo
            cb.SamplingDuration   = TimeSpan.FromSeconds(30);
            cb.BreakDuration      = TimeSpan.FromSeconds(15);
        }));
```

Si las respuestas 404 en operaciones de listado/descarga no deberían contar como fallos, configurá el circuit breaker para ignorar `StorageErrorCode.NotFound`:

```csharp
cb.ShouldHandle = result => result.ErrorCode != StorageErrorCode.NotFound;
```

---

## Archivo no encontrado al descargar pero existe en el bucket

**Síntoma:** `DownloadAsync` devuelve `NotFound`, pero podés ver el archivo en la consola del bucket o en la CLI.

**Causa:** El archivo fue subido con un `BucketOverride` diferente al bucket usado durante el request de descarga. Ambos requests parecen exitosos, pero apuntan a buckets diferentes.

**Solución:**

Asegurate de que `BucketOverride` sea consistente entre subida y descarga para el mismo archivo. Si almacenás la ruta resultante de `UploadAsync`, también almacená qué bucket fue usado:

```csharp
var uploadResult = await _storage.UploadAsync(new UploadRequest
{
    Path           = path,
    Content        = stream,
    BucketOverride = tenantBucket
});

// Almacenar tanto la ruta como el bucket
await _db.GuardarRegistroArchivo(new RegistroArchivo
{
    Ruta   = uploadResult.Value!.Path,
    Bucket = tenantBucket
});

// Más tarde, descargar usando el bucket almacenado
var downloadResult = await _storage.DownloadAsync(new DownloadRequest
{
    Path           = registro.Ruta,
    BucketOverride = registro.Bucket
});
```

---

## Compresión: el archivo descargado es ilegible / bytes basura

**Síntoma:** Un archivo subido con compresión habilitada se descarga como datos binarios ilegibles en lugar de contenido legible.

**Causa:** El archivo fue comprimido con GZip antes de la subida (por `CompressionMiddleware`), pero el cliente HTTP o el navegador no lo está descomprimiendo porque el header `Content-Encoding: gzip` no fue establecido en el objeto almacenado.

**Solución:**

Al subir, establecé la propiedad `ContentEncoding` en el request para que ValiBlob almacene el header junto con los metadatos del objeto:

```csharp
var request = new UploadRequest
{
    Path            = StoragePath.From("exports", "reporte.json"),
    Content         = stream,
    ContentType     = "application/json",
    ContentEncoding = "gzip"
};
```

Al servir el archivo a un navegador o cliente HTTP, incluí el header `Content-Encoding: gzip` en la respuesta HTTP. Si pasás el stream directamente al response, descomprimilo del lado del servidor:

```csharp
using var gzip = new GZipStream(downloadResult.Value!, CompressionMode.Decompress);
await gzip.CopyToAsync(httpContext.Response.Body);
```

---

## Azure: `BlobNotFoundException` después de que la subida parece exitosa

**Síntoma:** La subida devuelve éxito, pero una descarga o verificación de existencia posterior devuelve `BlobNotFound`.

**Causa:** El container no existe. El proveedor de Azure asume por defecto que el container ya fue creado y no lo creará automáticamente.

**Solución:**

Habilitá la creación automática del container:

```json
{
  "ValiBlob:Azure": {
    "Container": "my-files",
    "ConnectionString": "DefaultEndpointsProtocol=https;...",
    "CreateContainerIfNotExists": true
  }
}
```

O creá el container manualmente antes de iniciar la aplicación:

```bash
az storage container create \
  --name my-files \
  --account-name mystorageaccount \
  --auth-mode login
```

---

## OCI: `SetMetadata` devuelve `NotSupported`

**Síntoma:** Llamar a `SetMetadataAsync` en el proveedor OCI devuelve un error `NotSupported`.

**Causa:** Oracle Cloud Infrastructure Object Storage no soporta actualizar metadatos de objetos in-place después de que el objeto fue almacenado. El SDK de OCI no expone una operación standalone de set-metadata para objetos existentes.

**Solución alternativa:**

Re-subí el objeto con los metadatos deseados. El proveedor OCI de ValiBlob aplica los metadatos durante la llamada `PutObject`:

```csharp
// Descargar el archivo existente
var existing = await _storage.DownloadAsync(new DownloadRequest { Path = path });

// Re-subir con metadatos actualizados
await _storage.UploadAsync(new UploadRequest
{
    Path        = path,
    Content     = existing.Value!,
    ContentType = "application/pdf",
    Metadata    = new Dictionary<string, string>
    {
        ["procesado"] = "true",
        ["version"]   = "2"
    }
});
```

---

## Subida de archivos grandes con timeout

**Síntoma:** La subida de archivos mayores a algunos cientos de MB falla con un error de timeout.

**Causa:** El timeout de resiliencia por defecto es demasiado corto para transferencias de archivos grandes en conexiones lentas.

**Solución:**

Aumentá el timeout para operaciones de archivos grandes:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithResilience(r => r
        .Timeout(TimeSpan.FromMinutes(30)));
```

Para archivos mayores a 100 MB, cambiá directamente a subidas reanudables. Las subidas reanudables dividen el archivo en chunks para que ninguna llamada HTTP necesite transferir el payload completo:

```csharp
// Iniciar sesión
var session = await _resumable.StartUploadAsync(new ResumableUploadRequest
{
    FileName    = "dataset-grande.csv",
    ContentType = "text/csv",
    TotalSize   = fileInfo.Length
});

// Subir chunks
const int chunkSize = 8 * 1024 * 1024; // 8 MB
// ... loop de chunking ...

// Completar
await _resumable.CompleteUploadAsync(session.SessionId);
```

Ver [Subida reanudable](subida-reanudable.md) para la implementación completa.

---

## El health check siempre reporta `Unhealthy`

**Síntoma:** El endpoint `/healthz` reporta `Unhealthy` para uno o más proveedores de ValiBlob.

**Causa:** El health check realiza una prueba liviana contra el proveedor (generalmente una verificación de existencia del bucket o una operación de listado pequeña). Causas comunes de fallo:

- Las credenciales son incorrectas o expiraron.
- El bucket o container no existe.
- La conectividad de red al endpoint cloud está bloqueada.
- Al rol IAM o permiso le falta `ListBucket` / `GetBucketLocation`.

**Solución:**

1. Verificá los logs de tu aplicación para encontrar la excepción subyacente. ValiBlob registra la excepción completa bajo la categoría `ValiBlob.HealthChecks`.

2. Verificá las credenciales de forma independiente:

```bash
# AWS
aws s3 ls s3://my-bucket --region us-east-1

# Azure
az storage blob list --container-name my-container --account-name myaccount --auth-mode login

# GCP
gcloud storage ls gs://my-bucket
```

3. Asegurate de que el bucket exista y que la cuenta de servicio tenga como mínimo el permiso `ListBucket`.

4. Si se espera que el health check pase incluso cuando el bucket está vacío, confirmá que la prueba esté haciendo una operación de listado y no dependiendo de la existencia de un archivo específico.

5. Si un proveedor es opcional (por ejemplo, solo se usa en ciertos entornos), excluilo del health check obligatorio:

```csharp
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("required")
});
```

Y etiquetá tus checks al registrarlos:

```csharp
builder.Services
    .AddHealthChecks()
    .AddValiBlob("AWS", tags: new[] { "required" })
    .AddValiBlob("GCP", tags: new[] { "optional" });
```
