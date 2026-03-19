# Resolución de conflictos

Cuando una subida apunta a una ruta que ya existe en el storage, ValiBlob puede manejar el conflicto de tres formas, controladas por el enum `ConflictResolution` en el `UploadRequest`.

---

## El enum `ConflictResolution`

| Valor | Comportamiento |
|---|---|
| `Overwrite` (por defecto) | Reemplaza el archivo existente silenciosamente. No se realiza ninguna verificación de existencia. |
| `Rename` | Encuentra automáticamente la siguiente ruta disponible agregando un sufijo numérico. Usa un GUID como fallback si todos los candidatos numéricos están ocupados. |
| `Fail` | Lanza `StorageValidationException` si el archivo ya existe. |

---

## Configurar la resolución por solicitud

```csharp
var request = new UploadRequest
{
    Path = StoragePath.From("subidas", "reporte.pdf"),
    Content = fileStream,
    ContentType = "application/pdf",
    ConflictResolution = ConflictResolution.Rename
};

var result = await _storage.UploadAsync(request);

// result.Value.Path puede ser "subidas/reporte_1.pdf" si "subidas/reporte.pdf" ya existía
Console.WriteLine($"Guardado en: {result.Value!.Path}");
```

---

## `Overwrite` — reemplazo silencioso

El comportamiento por defecto. La subida procede sin verificar si la ruta destino existe. Idéntico al comportamiento de la mayoría de las APIs de almacenamiento cloud.

```csharp
var request = new UploadRequest
{
    Path = StoragePath.From("avatares", userId, "perfil.jpg"),
    Content = nuevoAvatarStream,
    ContentType = "image/jpeg",
    ConflictResolution = ConflictResolution.Overwrite // o simplemente omitirlo
};
```

Usalo cuando querés subidas idempotentes (por ejemplo, actualizaciones de foto de perfil, sincronización de archivos).

---

## `Rename` — renombrado automático seguro

El middleware agrega `_1`, `_2`, ... al nombre del archivo hasta encontrar una ruta disponible. Después de 1.000 intentos, se agrega un GUID para garantizar unicidad.

```
reporte.pdf     → existe
reporte_1.pdf   → existe
reporte_2.pdf   → disponible  ✓
```

Fallback con GUID (después de 1.000 colisiones):

```
reporte_3f1a2b4c9d8e7f6a5b3c2d1e0f9a8b7c.pdf
```

```csharp
var request = new UploadRequest
{
    Path = StoragePath.From("compartido", "documento.docx"),
    Content = docStream,
    ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    ConflictResolution = ConflictResolution.Rename
};

var result = await _storage.UploadAsync(request);
// La ruta final está disponible en result.Value!.Path
```

Usalo cuando múltiples usuarios pueden subir archivos con el mismo nombre y querés preservar todas las copias (por ejemplo, una carpeta compartida, una biblioteca de assets de CMS).

---

## `Fail` — detección explícita de conflicto

Si la ruta destino ya existe, la subida se cancela y se lanza `StorageValidationException`. Usalo cuando las subidas duplicadas representan un error que la lógica de tu aplicación debe manejar.

```csharp
var request = new UploadRequest
{
    Path = StoragePath.From("facturas", "FAC-2026-001.pdf"),
    Content = facturaStream,
    ContentType = "application/pdf",
    ConflictResolution = ConflictResolution.Fail
};

try
{
    var result = await _storage.UploadAsync(request);
}
catch (StorageValidationException)
{
    // Una factura con este número ya existe — mostrar error al usuario
    return Results.Conflict(new { error = "La factura FAC-2026-001 ya existe." });
}
```

Usalo para operaciones sensibles a la idempotencia: números de factura, IDs de orden, o cualquier caso donde una subida duplicada indica un error lógico y no una intención del usuario.

---

## Cuándo usar cada modo

| Escenario | Modo recomendado |
|---|---|
| Foto de perfil, avatar, imagen de portada | `Overwrite` |
| Sincronización de archivo de configuración | `Overwrite` |
| Uploads a CMS donde se deben conservar todas las versiones | `Rename` |
| Carpeta compartida con múltiples contribuidores | `Rename` |
| Factura o documento con identificador único | `Fail` |
| Pipeline de importación donde los duplicados son bugs | `Fail` |

---

## Nota de rendimiento

`Overwrite` no hace ninguna llamada de red para verificar existencia — es el modo más rápido. Tanto `Rename` como `Fail` emiten al menos una llamada `ExistsAsync` antes de la subida. `Rename` puede emitir hasta 1.001 llamadas en el peor caso (aunque en la práctica es extremadamente improbable). Para subidas masivas sensibles al rendimiento, preferí `Overwrite` o usá los `StoragePathExtensions` para generar rutas únicas antes de llamar a la subida.
