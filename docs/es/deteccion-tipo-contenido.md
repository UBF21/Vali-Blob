# Detección de tipo de contenido

El `ContentTypeDetectionMiddleware` inspecciona los primeros bytes (magic bytes) de cada archivo subido y establece — o sobreescribe — el campo `ContentType` en la solicitud. Esto evita que los clientes disfracen archivos peligrosos mintiendo sobre su extensión o tipo MIME.

---

## Para qué sirve

Las extensiones de archivo y los headers `Content-Type` enviados por el cliente son trivialmente falsificables. Un atacante puede renombrar `malware.exe` a `factura.jpg` y subirlo con `Content-Type: image/jpeg`. Sin inspección de magic bytes, ese archivo pasa cualquier lista de extensiones o tipos MIME permitidos.

El `ContentTypeDetectionMiddleware` lee los primeros 16 bytes del stream para detectar el formato real y sobreescribe (o establece) el `ContentType` antes de que cualquier middleware posterior — incluido el `ValidationMiddleware` — lo vea. Esto garantiza que tu lista de tipos permitidos aplique sobre formatos reales, no solo nombres de archivo.

---

## Formatos soportados

| Formato | Tipo MIME | Magic bytes |
|---|---|---|
| JPEG | `image/jpeg` | `FF D8 FF` |
| PNG | `image/png` | `89 50 4E 47` |
| GIF | `image/gif` | `47 49 46` |
| BMP | `image/bmp` | `42 4D` |
| TIFF (LE) | `image/tiff` | `49 49 2A 00` |
| TIFF (BE) | `image/tiff` | `4D 4D 00 2A` |
| PDF | `application/pdf` | `25 50 44 46` |
| ZIP / DOCX / XLSX | `application/zip` | `50 4B 03 04` |
| GZip | `application/gzip` | `1F 8B` |
| RAR | `application/x-rar` | `52 61 72 21` |
| MP4 | `video/mp4` | `ftyp` en offset 4 |
| MP3 (ID3) | `audio/mpeg` | `49 44 33` |
| MP3 (sync) | `audio/mpeg` | `FF FB` o `FF F3` |

Si los magic bytes no coinciden con ningún formato conocido, el `ContentType` permanece sin cambios.

---

## Configuración

| Propiedad | Tipo | Valor por defecto | Descripción |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Interruptor general de activación |
| `OverrideExisting` | `bool` | `false` | Si es `false`, la detección solo se ejecuta cuando `ContentType` es `null`. Si es `true`, siempre se ejecuta y sobreescribe el valor provisto por el cliente |

---

## Registro

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(p => p
        .WithContentTypeDetection(o =>
        {
            o.Enabled = true;
            o.OverrideExisting = true; // confiar siempre en los magic bytes, no en el cliente
        })
        .UseValidation(v =>
        {
            v.AllowedContentTypes = new[] { "image/jpeg", "image/png", "application/pdf" };
        })
    );
```

> Cuando `OverrideExisting` es `false` (el valor por defecto), el middleware actúa como fallback: establece el `ContentType` sólo cuando el cliente no lo proporcionó. Esto es seguro para clientes internos de confianza. Establecer `true` cuando las subidas provienen de clientes no confiables.

---

## Streams no seek-able

Cuando el stream de entrada no soporta `Seek` (por ejemplo, un socket de red o un stream comprimido), el middleware lee los primeros 16 bytes en un buffer y envuelve el stream original en un `LeadingBytesStream`. Este re-envía esos bytes al middleware posterior y al proveedor, sin perder ningún dato. No se requiere ningún manejo especial desde tu código.

---

## Combinación con ValidationMiddleware

Registrá el `ContentTypeDetectionMiddleware` **antes** que el `ValidationMiddleware` para que la validación vea el tipo MIME corregido:

```csharp
.WithPipeline(p => p
    .WithContentTypeDetection(o => o.OverrideExisting = true)  // 1. detectar tipo real
    .UseValidation(v =>
    {
        v.AllowedContentTypes = new[] { "image/jpeg", "image/png" }; // 2. aplicarlo
    })
)
```

Invertir este orden significa que el `ValidationMiddleware` verifica el `ContentType` enviado por el cliente (potencialmente falso) antes de que haya sido corregido.
