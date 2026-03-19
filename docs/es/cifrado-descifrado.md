# Cifrado y descifrado

ValiBlob soporta cifrado del lado del cliente AES-256-CBC de forma transparente. Los archivos se cifran antes de salir de tu aplicación y se descifran al descargarlos. No se requieren cambios en tu código de negocio más allá de la configuración inicial.

---

## Cómo funciona

El cifrado y el descifrado son manejados completamente dentro de ValiBlob, de forma transparente para el llamador.

```
Ruta de subida:
  Tu stream  →  [CompressionMiddleware]  →  EncryptionMiddleware  →  Proveedor cloud (bytes cifrados)

Ruta de descarga:
  Proveedor cloud (bytes cifrados)  →  BaseStorageProvider (descifra usando IV de metadatos)  →  Tu código (bytes originales)
```

### Subida

1. `EncryptionMiddleware` intercepta el stream de subida.
2. Se genera un IV aleatorio de 16 bytes para el archivo.
3. El stream se cifra con AES-256-CBC usando tu clave configurada y el IV generado.
4. El IV se almacena en los metadatos del archivo bajo `x-vali-iv` (codificado en base64).
5. También se escribe un marcador `x-vali-encrypted: AES-256-CBC` en los metadatos.
6. Los bytes cifrados se envían al proveedor de almacenamiento.

### Descarga

1. `BaseStorageProvider` obtiene el archivo y sus metadatos.
2. Detecta las claves de metadatos `x-vali-iv` y `x-vali-encrypted`.
3. Lee el IV y descifra el contenido con la clave configurada.
4. Tu código recibe el stream original sin cifrar.

El IV es por archivo: cada subida genera un IV diferente. Dos subidas del mismo contenido con la misma clave producen textos cifrados distintos.

---

## Configuración

### Registro en DI

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(p => p
        .UseValidation()
        .UseCompression()
        .UseEncryption(e =>
        {
            e.Enabled = true;
            e.Key = Convert.FromBase64String(
                builder.Configuration["ValiBlob:EncryptionKey"]!);
            // e.IV = null  →  IV aleatorio por subida (recomendado)
        })
    );
```

### Referencia de `EncryptionOptions`

| Propiedad | Tipo | Defecto | Descripción |
|---|---|---|---|
| `Enabled` | `bool` | `false` | Debe establecerse explícitamente en `true` para activar el cifrado |
| `Key` | `byte[]?` | `null` | Clave AES-256 — debe tener exactamente 32 bytes. Almacenala en secrets, nunca en `appsettings.json` |
| `IV` | `byte[]?` | `null` | IV de AES — 16 bytes. Si es `null`, se genera un IV aleatorio por subida (recomendado) |

---

## Generación de clave

Generá una clave de 32 bytes criptográficamente segura una vez y almacenala en tu gestor de secretos:

```csharp
using System.Security.Cryptography;

var key = new byte[32];
RandomNumberGenerator.Fill(key);
Console.WriteLine(Convert.ToBase64String(key));
// Ejemplo de salida: "k3Xr9pZw2mNqT7yVbL4sHcOuE6FdAiJg8RnW0ePxKYM="
// Almacená este valor en Azure Key Vault, AWS Secrets Manager o variable de entorno
```

Alternativamente, usando la clase `Aes`:

```csharp
using var aes = Aes.Create();
aes.KeySize = 256;
aes.GenerateKey();
Console.WriteLine("Key: " + Convert.ToBase64String(aes.Key));
```

---

## Gestión de claves

Nunca hardcodees la clave de cifrado en `appsettings.json` commiteado al repositorio.

### Variable de entorno (contenedores / CI)

```bash
ValiBlob__EncryptionKey=k3Xr9pZw2mNqT7yVbL4sHcOuE6FdAiJg8RnW0ePxKYM=
```

```csharp
e.Key = Convert.FromBase64String(Environment.GetEnvironmentVariable("ValiBlob__EncryptionKey")!);
```

### ASP.NET Core User Secrets (desarrollo local)

```bash
dotnet user-secrets set "ValiBlob:EncryptionKey" "k3Xr9pZw2mNqT7yVbL4sHcOuE6FdAiJg8RnW0ePxKYM="
```

### Azure Key Vault

```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri("https://my-vault.vault.azure.net/"),
    new DefaultAzureCredential());
```

Almacená la clave como un secreto llamado `ValiBlob--EncryptionKey`.

### AWS Secrets Manager

```csharp
builder.Configuration.AddSecretsManager(region: RegionEndpoint.USEast1, configurator: opts =>
{
    opts.SecretFilter = entry => entry.Name.StartsWith("valiblob/");
    opts.KeyGenerator = (entry, key) => key.Replace("valiblob/", "").Replace("/", ":");
});
```

---

## Ejemplo de ciclo completo

```csharp
// Subida — cifrada automáticamente por el pipeline
var uploadResult = await _storage.UploadAsync(new UploadRequest
{
    Path = StoragePath.From("documentos", "contrato.pdf"),
    Content = fileStream,
    ContentType = "application/pdf"
});

if (!uploadResult.IsSuccess)
    throw new Exception($"Error al subir: {uploadResult.ErrorMessage}");

// Descarga — descifrada automáticamente por el proveedor
var downloadResult = await _storage.DownloadAsync(new DownloadRequest
{
    Path = StoragePath.From("documentos", "contrato.pdf")
});

if (!downloadResult.IsSuccess)
    throw new Exception($"Error al descargar: {downloadResult.ErrorMessage}");

// downloadResult.Value contiene los bytes originales sin cifrar
using var output = File.Create("contrato-local.pdf");
await downloadResult.Value!.CopyToAsync(output);
```

---

## Cifrado y compresión combinados

Ambos middlewares pueden estar activos simultáneamente. El pipeline siempre los aplica en el orden correcto:

```
Subida:    comprimir  →  cifrar  →  almacenar
Descarga:  obtener  →  descifrar  →  descomprimir  →  devolver
```

Esto significa que la compresión es efectiva (se ejecuta sobre el contenido original, antes de que el cifrado lo convierta en bytes pseudoaleatorios). Ambas transformaciones son transparentes para el llamador.

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(p => p
        .UseValidation()
        .UseCompression()   // 1. Comprimir primero
        .UseEncryption(e => // 2. Cifrar los bytes comprimidos
        {
            e.Enabled = true;
            e.Key = Convert.FromBase64String(builder.Configuration["ValiBlob:EncryptionKey"]!);
        })
    );
```

> Registrar el cifrado antes de la compresión implica comprimir datos ya cifrados, que son esencialmente bytes aleatorios y no se comprimen. Siempre colocá `.UseCompression()` antes de `.UseEncryption()`.

---

## Qué se almacena en los metadatos

Cuando se sube un archivo con cifrado habilitado, ValiBlob escribe las siguientes entradas en los metadatos del archivo:

| Clave de metadato | Valor | Descripción |
|---|---|---|
| `x-vali-encrypted` | `AES-256-CBC` | Indica el algoritmo de cifrado utilizado |
| `x-vali-iv` | IV de 16 bytes codificado en base64 | El vector de inicialización necesario para el descifrado |

La clave de cifrado nunca se almacena. Solo se persiste el IV — la clave debe permanecer disponible en tu configuración.

---

## IV fijo vs IV aleatorio

| Modo | Configuración | Seguridad | Caso de uso |
|---|---|---|---|
| IV aleatorio por subida (defecto) | `IV = null` | Fuerte — archivos idénticos producen textos cifrados distintos | Recomendado para todos los escenarios de producción |
| IV fijo | `IV = new byte[16] { ... }` | Más débil — archivos idénticos producen textos cifrados idénticos | Solo para deduplicación determinista; entendé el compromiso |

> Usar un IV fijo debilita significativamente el cifrado cuando la misma clave se reutiliza en muchos archivos. Preferí el IV aleatorio en producción.

---

## Rotación de claves

Para rotar la clave de cifrado, re-cifrá los archivos existentes:

1. Configurá la clave antigua.
2. Descargá el archivo — el proveedor lo descifra con la clave antigua.
3. Configurá la clave nueva.
4. Volvé a subir el contenido descifrado — el pipeline lo cifra con la clave nueva.
5. Eliminá el archivo original solo después de confirmar que la re-subida fue exitosa.

Nunca elimines la clave antigua hasta que todos los archivos cifrados con ella hayan sido rotados.

---

## Propiedades de seguridad

- **Algoritmo:** AES-256-CBC con padding PKCS7
- **Tamaño de clave:** 256 bits (32 bytes)
- **Tamaño de IV:** 128 bits (16 bytes), generado con `System.Security.Cryptography.Aes`
- **Unicidad del IV:** Un IV por subida; nunca reutilizado entre archivos (usando el modo aleatorio por defecto)
- **Almacenamiento de clave:** Nunca persistida — solo el IV se almacena en los metadatos del archivo
- **Independiente del proveedor:** Funciona de forma idéntica con AWS, Azure, GCP, OCI, Supabase, Local e InMemory

---

## Limitaciones

- **Descargas por rango en archivos cifrados:** El proveedor debe descifrar el contenido completo antes de aplicar el rango de bytes. Las descargas por rango en archivos cifrados leen y descifran el archivo entero y luego devuelven el segmento solicitado.
- **Uso de memoria en archivos grandes:** El descifrado actualmente almacena en memoria el contenido completo. Evitá cifrar archivos muy grandes si la presión de memoria es una preocupación.
- **Sin bypass del cifrado del lado del servidor:** El cifrado del lado del cliente es independiente de cualquier cifrado del lado del servidor que ofrezca tu proveedor cloud. Ambos pueden estar activos simultáneamente, proporcionando defensa en profundidad, pero son independientes.
