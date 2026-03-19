# Guía de seguridad

Este documento cubre las características de seguridad integradas en ValiBlob y las prácticas que debés seguir al integrarlo en una aplicación de producción.

---

## Prevención de path traversal

El `ValidationMiddleware` de ValiBlob rechaza cualquier ruta de subida que contenga segmentos `..` antes de que el request llegue al proveedor. Esto evita que un atacante escape del prefijo de almacenamiento previsto y escriba en ubicaciones arbitrarias.

Rutas bloqueadas:

```
../secrets/config
documents/../../admin/keys.pem
uploads/../../../etc/passwd
```

Rutas permitidas:

```
documents/invoices/2024/inv-001.pdf
avatars/user-123/profile.jpg
reports/monthly/march.xlsx
```

`StoragePath` normaliza la ruta en el momento de construcción, eliminando barras duplicadas y resolviendo segmentos `.`. Siempre construí rutas con `StoragePath.From(...)` en lugar de strings crudos:

```csharp
// Correcto — normalizado y validado
var path = StoragePath.From("documents", userId, "report.pdf");

// Riesgoso — el string crudo omite los helpers de normalización
var path = new StoragePath($"documents/{userId}/report.pdf");
```

Si necesitás aceptar nombres de archivo provistos por el usuario, sanitizalos antes de construir la ruta:

```csharp
var safeName = Path.GetFileName(userSuppliedName); // elimina componentes de directorio
var path = StoragePath.From("uploads", tenantId, safeName);
```

---

## Gestión de credenciales

Nunca hardcodees credenciales cloud en el código fuente ni en `appsettings.json` commiteado al repositorio.

### Variables de entorno (recomendado para contenedores)

```json
{
  "ValiBlob:AWS": {
    "Bucket": "my-bucket",
    "Region": "us-east-1",
    "AccessKeyId": "",
    "SecretAccessKey": ""
  }
}
```

```bash
ValiBlob__AWS__AccessKeyId=AKIA...
ValiBlob__AWS__SecretAccessKey=wJalr...
```

### ASP.NET Core User Secrets (desarrollo local)

```bash
dotnet user-secrets set "ValiBlob:AWS:AccessKeyId" "AKIA..."
dotnet user-secrets set "ValiBlob:AWS:SecretAccessKey" "wJalr..."
```

### Azure Key Vault (producción)

```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri("https://my-vault.vault.azure.net/"),
    new DefaultAzureCredential());
```

Almacená los secrets como `ValiBlob--AWS--AccessKeyId` (el doble guión mapea al separador de dos puntos).

### AWS Secrets Manager (producción)

```csharp
builder.Configuration.AddSecretsManager(region: RegionEndpoint.USEast1, configurator: opts =>
{
    opts.SecretFilter = entry => entry.Name.StartsWith("valiblob/");
    opts.KeyGenerator = (entry, key) => key.Replace("valiblob/", "").Replace("/", ":");
});
```

En EC2 / ECS / Lambda, preferí roles IAM para que no sean necesarias credenciales estáticas — dejá `AccessKeyId` y `SecretAccessKey` vacíos y el SDK de AWS tomará el instance profile automáticamente.

---

## Cifrado del lado del cliente

El `EncryptionMiddleware` de ValiBlob cifra el contenido del archivo con AES-256-CBC antes de que salga de tu aplicación. Incluso si un bucket está mal configurado o las credenciales se ven comprometidas, los bytes almacenados son ilegibles sin la clave.

### Configuración

```csharp
builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseAWS()
    .WithPipeline(p => p
        .UseEncryption(e =>
        {
            e.Key = Convert.FromBase64String(Environment.GetEnvironmentVariable("STORAGE_ENC_KEY")!);
            e.IV  = Convert.FromBase64String(Environment.GetEnvironmentVariable("STORAGE_ENC_IV")!);
        }));
```

### Generar una clave e IV seguros

```csharp
using System.Security.Cryptography;

using var aes = Aes.Create();
aes.KeySize = 256;
aes.GenerateKey();
aes.GenerateIV();

Console.WriteLine("Key: " + Convert.ToBase64String(aes.Key));
Console.WriteLine("IV:  " + Convert.ToBase64String(aes.IV));
```

> **Advertencia:** Un IV fijo reutiliza el mismo vector de inicialización para cada archivo. Esto es aceptable para datos cifrados en reposo donde los archivos son independientes, pero reduce las garantías de confidencialidad respecto a un IV aleatorio por archivo. Si tu modelo de amenaza requiere IVs por archivo, implementá un `IEncryptionMiddleware` personalizado que anteponga el IV aleatorio al texto cifrado y lo elimine al descargar.

### Rotación de claves

Al rotar la clave de cifrado, los archivos existentes deben ser re-cifrados. Un procedimiento de rotación seguro:

1. Descargá el archivo con la clave anterior configurada.
2. Volvé a subir el archivo con la clave nueva configurada.
3. Eliminá el archivo antiguo después de confirmar que la re-subida fue exitosa.

Nunca elimines la clave antigua hasta que todos los archivos cifrados con ella hayan sido rotados.

---

## Validación de checksum en chunks

Al usar subidas reanudables, habilitá la validación de checksum para detectar corrupción de datos en tránsito:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithResumableUploads(r => r
        .EnableChecksumValidation());
```

Para cada chunk, proporcioná el hash MD5 esperado:

```csharp
var chunkRequest = new ResumableChunkRequest
{
    SessionId   = sessionId,
    ChunkIndex  = 0,
    Data        = chunkStream,
    ExpectedMd5 = ComputarMd5(chunkBytes) // tu helper
};

var result = await _resumable.UploadChunkAsync(chunkRequest);
```

Si el MD5 del chunk recibido no coincide con `ExpectedMd5`, ValiBlob devuelve un `StorageResult` fallido con un mensaje de error apropiado. Reintentá el chunk en lugar de continuar — un chunk corrupto causará que el archivo ensamblado sea inválido.

---

## Seguridad de URLs prefirmadas

Las URLs prefirmadas otorgan acceso temporal a un objeto específico sin que el llamador necesite credenciales cloud. Seguí estas pautas:

- **Establecé expiraciones cortas.** Para la mayoría de los casos de uso, 5–15 minutos es suficiente. Nunca emitas URLs prefirmadas sin expiración.
- **Generá por request.** No caches ni reutilices URLs prefirmadas. Cada operación debe producir una URL nueva para el destinatario previsto.
- **Distinguí URLs de subida y descarga.** Una URL prefirmada de subida solo debe usarse para escribir un objeto específico. Una URL de descarga solo debe usarse para leer. No uses la misma URL para ambas direcciones.
- **Auditá la emisión de URLs.** Registrá quién solicitó una URL prefirmada, para qué objeto y cuándo. Esto te permite detectar patrones de abuso.

```csharp
// URL prefirmada de subida — expira en 10 minutos
var uploadUrl = await _presigned.GetPresignedUploadUrlAsync(new PresignedUrlRequest
{
    Path       = StoragePath.From("uploads", Guid.NewGuid().ToString()),
    Expiration = TimeSpan.FromMinutes(10)
});

// URL prefirmada de descarga — expira en 5 minutos
var downloadUrl = await _presigned.GetPresignedDownloadUrlAsync(new PresignedUrlRequest
{
    Path       = StoragePath.From("documents", "factura-001.pdf"),
    Expiration = TimeSpan.FromMinutes(5)
});
```

---

## Aislamiento de buckets

Usá `BucketOverride` para forzar aislamiento por tenant a nivel de request:

```csharp
var request = new UploadRequest
{
    Path           = StoragePath.From("reports", fileName),
    Content        = stream,
    ContentType    = "application/pdf",
    BucketOverride = $"tenant-{tenantId}-files"
};
```

Para un aislamiento multi-tenant estricto, derivá el nombre del bucket de un identificador de tenant verificado — nunca directamente desde input del usuario. Validá que el nombre de bucket resuelto esté en tu lista de permitidos antes de emitir el request:

```csharp
private string ResolverBucket(string tenantId)
{
    if (!_tenantsPermitidos.Contains(tenantId))
        throw new UnauthorizedAccessException($"Tenant desconocido: {tenantId}");

    return $"tenant-{tenantId}-files";
}
```

Ver [Multi-tenant](multi-tenant.md) para estrategias completas de aislamiento.

---

## Principio de mínimo privilegio

Otorgá a tu aplicación solo los permisos que necesita. Evitá usar credenciales de root o admin.

### AWS IAM — política S3 mínima

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject",
        "s3:DeleteObject",
        "s3:ListBucket"
      ],
      "Resource": [
        "arn:aws:s3:::my-app-bucket",
        "arn:aws:s3:::my-app-bucket/*"
      ]
    }
  ]
}
```

Si se necesitan URLs prefirmadas, también agregá `s3:GetObjectAttributes` y remové la restricción de ARN de objeto explícita para la acción `ListBucket`.

### Azure RBAC

Asigná el rol `Storage Blob Data Contributor` con scope en el container de almacenamiento específico, no en toda la cuenta de almacenamiento:

```
Scope: /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Storage/storageAccounts/{account}/blobServices/default/containers/{container}
Role:  Storage Blob Data Contributor
```

Para cargas de trabajo de solo lectura, usá `Storage Blob Data Reader` en su lugar.

### GCP IAM

Asigná `roles/storage.objectAdmin` con scope en el bucket específico, no en el proyecto:

```bash
gcloud storage buckets add-iam-policy-binding gs://my-app-bucket \
  --member="serviceAccount:my-app@my-project.iam.gserviceaccount.com" \
  --role="roles/storage.objectAdmin"
```

Para generar URLs prefirmadas, la cuenta de servicio también necesita `roles/iam.serviceAccountTokenCreator` sobre sí misma.

---

## Rate limiting y prevención de abuso

ValiBlob no implementa rate limiting — esto corresponde a tu capa de API, antes de llamar a ValiBlob. Sin rate limiting, un atacante puede agotar tu cuota de egreso cloud, generar facturas elevadas o saturar tu red.

Enfoque recomendado con ASP.NET Core:

```csharp
builder.Services.AddRateLimiter(opts =>
{
    opts.AddFixedWindowLimiter("upload", o =>
    {
        o.Window           = TimeSpan.FromMinutes(1);
        o.PermitLimit      = 20;
        o.QueueLimit       = 0;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

// En tu endpoint
app.MapPost("/files", UploadHandler)
   .RequireRateLimiting("upload");
```

Para los endpoints de subidas reanudables, aplicá rate limiting tanto al endpoint de creación de sesión como a cada endpoint de subida de chunk por separado.
