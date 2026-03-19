# Pipeline de Middleware

El pipeline de ValiBlob es inspirado directamente en el middleware de ASP.NET Core: una cadena de componentes que se ejecutan en orden antes de que el contenido llegue al proveedor. Cada middleware puede transformar el request, validarlo, o cortocircuitar la operación.

---

## Concepto

Cuando llamás a `UploadAsync`, el contenido no va directamente al proveedor cloud. Primero atraviesa todos los middlewares registrados en orden:

```
UploadAsync(request)
        │
        ▼
┌─────────────────────┐
│  ValidationMiddleware│  ← valida extensión, tamaño, content-type
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│ CompressionMiddleware│  ← comprime si aplica (gzip)
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  EncryptionMiddleware│  ← cifra con AES-256-CBC
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  [tu middleware]    │  ← cualquier lógica personalizada
└──────────┬──────────┘
           │
           ▼
     Proveedor cloud
     (AWS/Azure/GCP...)
```

Si algún middleware detecta un problema (por ejemplo, extensión bloqueada), llama al delegado `next` nunca se ejecuta — el pipeline se corta y se retorna un `StorageResult` de falla.

---

## Middlewares incluidos

| Middleware | Método de registro | Descripción |
|---|---|---|
| `ValidationMiddleware` | `.UseValidation()` | Valida extensión, tamaño y content-type |
| `CompressionMiddleware` | `.UseCompression()` | Comprime automáticamente tipos comprimibles |
| `EncryptionMiddleware` | `.UseEncryption()` | Cifra el contenido con AES-256-CBC |

---

## Registro del pipeline

El pipeline se configura dentro de `WithPipeline` en el builder de ValiBlob:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(pipeline =>
    {
        pipeline
            .UseValidation(opts =>
            {
                opts.MaxFileSizeBytes = 100 * 1024 * 1024; // 100 MB
                opts.AllowedExtensions = [".pdf", ".png", ".jpg", ".docx"];
                opts.BlockedExtensions = [".exe", ".bat", ".sh"];
            })
            .UseCompression(opts =>
            {
                opts.Enabled = true;
                opts.MinSizeBytes = 4096;
            })
            .UseEncryption(opts =>
            {
                opts.Enabled = true;
                opts.Key = Convert.FromBase64String(configuration["Storage:EncryptionKey"]!);
            });
    })
    .WithDefaultProvider("AWS");
```

---

## ValidationMiddleware

Valida cada `UploadRequest` antes de que el contenido toque el proveedor. Si la validación falla, retorna `StorageErrorCode.ValidationFailed` y el upload nunca ocurre.

### Opciones (`ValidationOptions`)

| Propiedad | Tipo | Default | Descripción |
|---|---|---|---|
| `MaxFileSizeBytes` | `long` | `524_288_000` (500 MB) | Tamaño máximo del archivo en bytes |
| `AllowedExtensions` | `IList<string>` | `[]` (vacío = todo permitido) | Si tiene valores, sólo estas extensiones son aceptadas |
| `BlockedExtensions` | `IList<string>` | `[".exe", ".bat", ".cmd", ".sh"]` | Extensiones siempre rechazadas, incluso si `AllowedExtensions` está vacío |
| `AllowedContentTypes` | `IList<string>` | `[]` (vacío = todo permitido) | Si tiene valores, sólo estos content-types son aceptados |

### Lógica de validación

1. Si `AllowedExtensions` no está vacío y la extensión del archivo **no está** en la lista → falla
2. Si la extensión del archivo **está** en `BlockedExtensions` → falla (incluso si `AllowedExtensions` está vacío)
3. Si `ContentLength` supera `MaxFileSizeBytes` → falla
4. Si `AllowedContentTypes` no está vacío y el `ContentType` **no está** en la lista → falla

### Ejemplo de configuración estricta

```csharp
.UseValidation(opts =>
{
    opts.MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB máximo
    opts.AllowedExtensions = new List<string> { ".pdf", ".png", ".jpg" };
    opts.AllowedContentTypes = new List<string>
    {
        "application/pdf",
        "image/png",
        "image/jpeg"
    };
})
```

### Validar resultado

```csharp
var result = await _storage.UploadAsync(request);

if (!result.IsSuccess && result.ErrorCode == StorageErrorCode.ValidationFailed)
{
    Console.WriteLine($"Validación fallida: {result.ErrorMessage}");
    // Ej: "File extension '.exe' is not allowed."
}
```

---

## CompressionMiddleware

Comprime automáticamente el contenido del upload usando GZip cuando el content-type es comprimible y el archivo supera el tamaño mínimo. El middleware reemplaza el `Stream` en el contexto por un stream comprimido transparentemente.

### Opciones (`CompressionOptions`)

| Propiedad | Tipo | Default | Descripción |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Activa o desactiva la compresión |
| `MinSizeBytes` | `int` | `1024` (1 KB) | Tamaño mínimo para comprimir. Archivos más pequeños no se comprimen |
| `CompressibleContentTypes` | `IList<string>` | Ver abajo | Content-types que se comprimen |

**Content-types comprimibles por defecto:**
- `text/plain`
- `text/html`
- `text/css`
- `text/xml`
- `application/json`
- `application/xml`
- `application/javascript`

### Cuándo se activa la compresión

El middleware comprime cuando se cumplen **todas** estas condiciones:
1. `Enabled = true`
2. El `ContentType` del request está en `CompressibleContentTypes`
3. `ContentLength` es mayor a `MinSizeBytes` (si se provee)

### Agregar content-types personalizados

```csharp
.UseCompression(opts =>
{
    opts.CompressibleContentTypes.Add("application/x-ndjson");
    opts.CompressibleContentTypes.Add("text/csv");
    opts.MinSizeBytes = 2048; // sólo comprimir si > 2 KB
})
```

> **💡 Tip:** No comprimas imágenes (PNG, JPEG, WebP) ni videos — ya están comprimidos internamente. Agregar una capa GZip sobre ellos aumentaría el tamaño. El middleware excluye estos tipos por defecto al no tenerlos en la lista.

---

## EncryptionMiddleware

Cifra el contenido del upload con **AES-256-CBC** antes de enviarlo al proveedor. El descifrado es responsabilidad del cliente — ValiBlob no descifra automáticamente en downloads.

### Opciones (`EncryptionOptions`)

| Propiedad | Tipo | Default | Descripción |
|---|---|---|---|
| `Enabled` | `bool` | `false` | Activa el cifrado |
| `Key` | `byte[]?` | `null` | Clave AES-256 — exactamente 32 bytes |
| `IV` | `byte[]?` | `null` | Vector de inicialización — 16 bytes. Si es `null`, se genera un IV aleatorio por archivo |

### Manejo de la clave

La clave **nunca debe estar en `appsettings.json`**. Usá siempre un sistema de secretos:

```csharp
// Opción A: desde user-secrets (desarrollo)
// dotnet user-secrets set "Storage:EncryptionKey" "base64encodedKey"

// Opción B: desde variable de entorno
var keyBase64 = Environment.GetEnvironmentVariable("STORAGE_ENCRYPTION_KEY")!;

builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(pipeline =>
    {
        pipeline.UseEncryption(opts =>
        {
            opts.Enabled = true;
            opts.Key = Convert.FromBase64String(keyBase64);
            // IV = null → IV aleatorio por archivo (recomendado)
        });
    });
```

### Generar una clave AES-256

```csharp
// Generar una clave aleatoria de 32 bytes y mostrarla en Base64
using var aes = System.Security.Cryptography.Aes.Create();
aes.KeySize = 256;
aes.GenerateKey();
Console.WriteLine(Convert.ToBase64String(aes.Key));
// Guardá este valor en tu gestor de secretos
```

### IV aleatorio vs IV fijo

| Modo | Cuándo usar |
|---|---|
| `IV = null` (aleatorio, recomendado) | La mayoría de los casos. Cada archivo tiene su propio IV, lo que garantiza que dos archivos idénticos producen ciphertext diferente |
| `IV` fijo | Cuando necesitás deduplicación por contenido: dos uploads del mismo archivo producen el mismo ciphertext, lo que permite detectar duplicados por hash del ciphertext |

### Descifrar en el cliente

Dado que ValiBlob no descifra automáticamente, necesitás hacerlo en tu código:

```csharp
public async Task<byte[]> DescargarYDescifrarAsync(string path, byte[] key, byte[] iv)
{
    var result = await _storage.DownloadAsync(new DownloadRequest
    {
        Path = StoragePath.From(path)
    });

    if (!result.IsSuccess)
        throw new Exception($"Error al descargar: {result.ErrorMessage}");

    using var aes = System.Security.Cryptography.Aes.Create();
    aes.Key = key;
    aes.IV = iv;
    aes.Mode = System.Security.Cryptography.CipherMode.CBC;

    using var decryptor = aes.CreateDecryptor();
    using var cryptoStream = new System.Security.Cryptography.CryptoStream(
        result.Value!, decryptor, System.Security.Cryptography.CryptoStreamMode.Read);
    using var output = new MemoryStream();
    await cryptoStream.CopyToAsync(output);
    return output.ToArray();
}
```

---

## Escribir un middleware personalizado

Implementá `IStorageMiddleware` para agregar lógica propia al pipeline:

```csharp
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Pipeline;

public sealed class AuditUploadMiddleware : IStorageMiddleware
{
    private readonly ILogger<AuditUploadMiddleware> _logger;

    public AuditUploadMiddleware(ILogger<AuditUploadMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next)
    {
        // Lógica ANTES de continuar el pipeline
        _logger.LogInformation(
            "Iniciando upload de {Path} ({Size} bytes)",
            context.Request.Path,
            context.Request.ContentLength);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Continuar con el siguiente middleware / proveedor
        await next(context);

        sw.Stop();

        // Lógica DESPUÉS del pipeline
        _logger.LogInformation(
            "Upload de {Path} completado en {Ms}ms",
            context.Request.Path,
            sw.ElapsedMilliseconds);
    }
}
```

### Middleware que modifica el request

```csharp
public sealed class TenantPrefixMiddleware : IStorageMiddleware
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantPrefixMiddleware(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next)
    {
        var tenantId = _httpContextAccessor.HttpContext?.User.FindFirst("tenant_id")?.Value;

        if (!string.IsNullOrEmpty(tenantId))
        {
            // Reemplazar el request con uno que tiene el prefijo del tenant
            var originalPath = context.Request.Path;
            var tenantPath = StoragePath.From("tenants", tenantId) / originalPath.FileName;
            context.Request = context.Request.WithContent(context.Request.Content);
            // Nota: en un escenario real, también modificarías el Path en el contexto
        }

        await next(context);
    }
}
```

### Middleware que cortocircuita el pipeline

```csharp
public sealed class RateLimitMiddleware : IStorageMiddleware
{
    private readonly IRateLimiter _rateLimiter;

    public RateLimitMiddleware(IRateLimiter rateLimiter)
    {
        _rateLimiter = rateLimiter;
    }

    public async Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next)
    {
        if (!await _rateLimiter.TryAcquireAsync())
        {
            // Cortocircuitar — no llamar a next
            context.Result = StorageResult<UploadResult>.Failure(
                "Límite de tasa excedido. Intente de nuevo en unos segundos.",
                StorageErrorCode.ProviderError);
            return;
        }

        await next(context);
    }
}
```

---

## Registrar middleware personalizado

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(pipeline =>
    {
        pipeline
            .UseValidation()
            .UseCompression()
            .Use<AuditUploadMiddleware>() // tu middleware personalizado
            .Use<RateLimitMiddleware>();  // otro middleware personalizado
    });
```

---

## El orden del pipeline importa

El orden en que registrás los middlewares es el orden en que se ejecutan. El orden recomendado es:

```
Validación → Compresión → Cifrado → [middlewares propios] → Proveedor
```

**¿Por qué este orden?**

1. **Validación primero**: Rechazá lo más temprano posible para no gastar CPU en comprimir/cifrar algo que va a ser rechazado de todas formas.

2. **Compresión antes que cifrado**: Cifrar datos comprimidos es más eficiente (menos bytes a cifrar). Comprimir datos cifrados es casi imposible (el cifrado destruye los patrones que la compresión necesita).

3. **Middlewares propios antes del proveedor**: Tus middlewares de auditoría, telemetría, enriquecimiento de metadata, etc. van después del cifrado para que trabajen sobre el contenido final que va al proveedor.

```csharp
// CORRECTO — orden óptimo
.WithPipeline(pipeline => pipeline
    .UseValidation()     // 1. Rechazar lo antes posible
    .UseCompression()    // 2. Reducir tamaño
    .UseEncryption()     // 3. Cifrar el contenido comprimido
    .Use<AuditMiddleware>()) // 4. Auditar el request final

// INCORRECTO — compresión sobre datos cifrados (no comprime casi nada)
.WithPipeline(pipeline => pipeline
    .UseValidation()
    .UseEncryption()
    .UseCompression()) // ← comprime datos aleatorios = sin beneficio
```
