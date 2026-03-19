# Procesamiento de imágenes

`ValiBlob.ImageSharp` agrega un middleware de procesamiento de imágenes al pipeline de subida. Puede redimensionar imágenes, convertirlas a un formato diferente (JPEG, PNG, WebP) y generar miniaturas automáticamente — todo en una única operación de subida.

---

## Instalación

```bash
dotnet add package ValiBlob.ImageSharp
```

---

## Registro

```csharp
using ValiBlob.ImageSharp.DependencyInjection;

builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(p => p
        .WithImageProcessing(o =>
        {
            o.MaxWidth = 1920;
            o.MaxHeight = 1080;
            o.JpegQuality = 85;
        })
    );
```

---

## `ImageProcessingOptions`

| Propiedad | Tipo | Valor por defecto | Descripción |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Interruptor general de activación |
| `MaxWidth` | `int?` | `null` | Ancho máximo de salida en píxeles. `null` = sin límite |
| `MaxHeight` | `int?` | `null` | Alto máximo de salida en píxeles. `null` = sin límite |
| `JpegQuality` | `int` | `85` | Calidad de codificación JPEG (1–100) |
| `OutputFormat` | `ImageOutputFormat?` | `null` | Convertir a `Jpeg`, `Png` o `Webp`. `null` = mantener formato original |
| `ProcessableContentTypes` | `HashSet<string>` | Ver abajo | Tipos MIME que activan el procesamiento |
| `Thumbnail` | `ThumbnailOptions?` | `null` | Configuración de generación de miniaturas. `null` = desactivado |

Tipos de contenido procesables por defecto: `image/jpeg`, `image/png`, `image/gif`, `image/bmp`, `image/webp`, `image/tiff`.

### `ThumbnailOptions`

| Propiedad | Tipo | Valor por defecto | Descripción |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Si se genera la miniatura |
| `Width` | `int` | `200` | Ancho de la miniatura en píxeles |
| `Height` | `int` | `200` | Alto de la miniatura en píxeles |
| `Suffix` | `string` | `"_thumb"` | Sufijo agregado al nombre del archivo. `foto.jpg` → `foto_thumb.jpg` |

### `ImageOutputFormat`

| Valor | Tipo MIME de salida |
|---|---|
| `Jpeg` | `image/jpeg` |
| `Png` | `image/png` |
| `Webp` | `image/webp` |

---

## Ejemplos

### Solo redimensionar — mantener formato original

```csharp
.WithImageProcessing(o =>
{
    o.MaxWidth = 2048;
    o.MaxHeight = 2048;
    // OutputFormat es null → formato sin cambios
})
```

El redimensionado usa `ResizeMode.Max`: la imagen se escala proporcionalmente para encajar en el cuadro. Una imagen que ya encaja dentro de los límites no se amplía.

### Convertir todas las subidas a WebP

```csharp
.WithImageProcessing(o =>
{
    o.MaxWidth = 1920;
    o.MaxHeight = 1080;
    o.OutputFormat = ImageOutputFormat.Webp;
})
```

El `ContentType` de la solicitud se actualiza a `image/webp` después de la conversión, para que el proveedor almacene el tipo MIME correcto.

### Generar miniaturas

```csharp
.WithImageProcessing(o =>
{
    o.MaxWidth = 1280;
    o.JpegQuality = 80;
    o.Thumbnail = new ThumbnailOptions
    {
        Enabled = true,
        Width = 300,
        Height = 300,
        Suffix = "_thumb"
    };
})
```

Cuando la generación de miniaturas está activa, el middleware:

1. Procesa y sube la imagen principal por el pipeline normalmente.
2. Después de que la subida principal se completa, genera una miniatura JPEG con las dimensiones especificadas.
3. Sube la miniatura al mismo proveedor de storage en una ruta derivada: `{dir}/{nombre}{sufijo}.jpg`.

Por ejemplo, subir `productos/silla.png` también crea `productos/silla_thumb.jpg`.

Los fallos en la generación de miniaturas son **no fatales** — un error durante la generación se ignora silenciosamente para que la subida principal no se vea afectada.

### Restringir el procesamiento a formatos específicos

```csharp
.WithImageProcessing(o =>
{
    o.MaxWidth = 800;
    o.ProcessableContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png"
        // GIF, BMP, TIFF, WebP pasarán sin modificación
    };
})
```

Los archivos cuyo `ContentType` no esté en `ProcessableContentTypes` pasan por el middleware sin modificación.

---

## Posición en el pipeline

Colocá `WithImageProcessing` después del `ContentTypeDetectionMiddleware` (para que el tipo MIME sea preciso) y antes de cualquier validación que verifique dimensiones o tamaño de imagen:

```csharp
.WithPipeline(p => p
    .WithContentTypeDetection(o => o.OverrideExisting = true)
    .WithImageProcessing(o =>
    {
        o.MaxWidth = 1920;
        o.OutputFormat = ImageOutputFormat.Webp;
    })
    .UseValidation(v =>
    {
        v.AllowedContentTypes = new[] { "image/webp" }; // validar el tipo de salida
    })
)
```

---

## Nota de rendimiento

Las imágenes se cargan y procesan completamente en memoria usando la librería [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp). Para imágenes grandes o escenarios de alta concurrencia, esto puede tener un impacto significativo en el uso de memoria. Considerá:

- Establecer límites razonables de `MaxWidth` / `MaxHeight` para acotar el tamaño de salida.
- Delegar el procesamiento de imágenes a un job en background para archivos muy grandes.
- Monitorear la presión del heap bajo carga y ajustar los límites de memoria de la aplicación según corresponda.
