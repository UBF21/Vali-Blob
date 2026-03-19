# Deduplicación

El `DeduplicationMiddleware` calcula un hash SHA-256 del contenido del archivo antes de cada subida, almacena ese hash en los metadatos del archivo y — opcionalmente — cancela la subida cuando ya existe un archivo idéntico en el almacenamiento.

La deduplicación es **opt-in**: está desactivada por defecto porque escanear los archivos existentes tiene un costo de rendimiento.

---

## Cómo funciona

1. El middleware lee todo el stream de contenido y calcula su hash SHA-256.
2. El stream se rebobina a la posición 0 (si soporta seek) para que el middleware posterior y el proveedor puedan leerlo normalmente.
3. El hash se almacena en `context.Items["deduplication.contentHash"]` para inspección por middleware posterior.
4. El hash se estampa en los metadatos de la solicitud bajo la clave configurada por `MetadataHashKey` (por defecto `x-content-hash`). Esto hace que cada archivo subido sea identificable por su huella de contenido.
5. Cuando `CheckBeforeUpload` es `true`, el middleware lista todos los archivos en el storage y lee sus metadatos buscando uno cuyo `x-content-hash` coincida. Si lo encuentra, cancela el pipeline y expone la ruta existente a través de `context.Items`.

---

## Configuración

| Propiedad | Tipo | Valor por defecto | Descripción |
|---|---|---|---|
| `Enabled` | `bool` | `false` | Opt-in: debe establecerse explícitamente en `true` |
| `CheckBeforeUpload` | `bool` | `true` | Si es `true`, busca un duplicado y cancela si existe uno |
| `MetadataHashKey` | `string` | `"x-content-hash"` | La clave de metadatos donde se almacena el hash |

---

## Registro

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(p => p
        .WithDeduplication(o =>
        {
            o.Enabled = true;
            o.CheckBeforeUpload = true;
            o.MetadataHashKey = "x-content-hash"; // valor por defecto
        })
    );
```

---

## Manejo de duplicados en el código de aplicación

Cuando se detecta un duplicado, el pipeline establece `context.IsCancelled = true` y retorna sin subir. El resultado de `UploadAsync` refleja un estado cancelado. Podés inspeccionar los items del contexto para obtener la ruta del archivo existente:

```csharp
// El middleware popula estos items en el StoragePipelineContext antes de retornar.
// Leelos desde tu propio middleware o event handler.

if (context.Items.TryGetValue("deduplication.isDuplicate", out var isDup) && isDup is true)
{
    var existingPath = context.Items["deduplication.existingPath"] as string;
    // Redirigir al usuario al archivo existente en lugar de subir una nueva copia
    return Results.Ok(new { path = existingPath, duplicate = true });
}
```

Cuando `CheckBeforeUpload` es `false`, la subida continúa independientemente de si existe una copia. El hash se estampa igual en los metadatos para que futuras subidas puedan detectar duplicados contra este archivo.

---

## Limitaciones

### Detección basada en escaneo

La detección de duplicados funciona listando todos los archivos y leyendo sus metadatos uno por uno. Esta es una operación **O(n)**: emite una llamada `GetMetadata` por cada archivo en el storage. Para buckets grandes esto puede ser lento y costoso.

**Recomendaciones:**

- Usar deduplicación en buckets acotados (por ejemplo, por usuario o por tenant) en lugar de un único bucket global.
- Considerar construir un índice de hashes en una base de datos e implementar `CheckBeforeUpload = false` combinado con una búsqueda personalizada en la capa de aplicación.
- Si el proveedor de storage soporta consultas de metadatos del lado del servidor (por ejemplo, S3 Select, consultas en Azure Table), implementar un `IStorageMiddleware` personalizado que aproveche esa capacidad.

### Streams no seek-able

Si el stream de contenido no soporta seek, el cálculo SHA-256 consume el stream. Después de hashear, el stream no puede rebobinarse y la subida fallará. Asegurate de proporcionar streams con seek (por ejemplo, `MemoryStream` o `FileStream`) al usar el `DeduplicationMiddleware`.
