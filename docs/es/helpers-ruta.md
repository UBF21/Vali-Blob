# Helpers de ruta de storage

`StoragePathExtensions` provee cinco métodos de extensión sobre `StoragePath` para transformaciones de ruta habituales: prefijos de fecha, sufijos de hash, sufijos aleatorios y saneamiento. Todos los métodos son puros — devuelven un nuevo `StoragePath` y no modifican el original.

---

## Métodos de un vistazo

| Método | Ejemplo de salida |
|---|---|
| `WithDatePrefix()` | `2026/03/17/foto.jpg` |
| `WithTimestampPrefix()` | `2026/03/17/14-30-00/foto.jpg` |
| `WithHashSuffix(contenido)` | `foto_a3f2b1c4.jpg` |
| `WithRandomSuffix()` | `foto_5f3a2b1c.jpg` |
| `Sanitize()` | `mi_documento_v2.pdf` |

---

## `WithDatePrefix()`

Agrega un prefijo `yyyy/MM/dd` basado en la fecha UTC actual.

```csharp
var ruta = StoragePath.From("foto.jpg").WithDatePrefix();
// → StoragePath("2026/03/17/foto.jpg")
```

Útil para organizar archivos por fecha de subida, habilitando políticas de ciclo de vida eficientes y consultas por rango de fechas.

---

## `WithTimestampPrefix()`

Agrega un prefijo `yyyy/MM/dd/HH-mm-ss` basado en la fecha y hora UTC actual.

```csharp
var ruta = StoragePath.From("exportacion.csv").WithTimestampPrefix();
// → StoragePath("2026/03/17/14-30-00/exportacion.csv")
```

Usalo cuando necesitás precisión al segundo — por ejemplo, para agrupar archivos por el batch o job que los creó, o para proporcionar un orden de listado cronológico natural.

---

## `WithHashSuffix(contenido)`

Agrega una cadena hexadecimal corta de 8 caracteres derivada del hash SHA-256 del string `contenido` proporcionado.

```csharp
var ruta = StoragePath.From("foto.jpg").WithHashSuffix("usuario-42");
// → StoragePath("foto_a3f2b1c4.jpg")
```

El hash se calcula a partir del parámetro `contenido` (un string que vos suministrás — típicamente un ID de usuario, ID de sesión, o el hash del propio contenido del archivo). Solo se usan los primeros 4 bytes del hash SHA-256, produciendo un sufijo de 8 caracteres.

Usalo para crear rutas deterministas: el mismo string `contenido` siempre produce el mismo sufijo, lo cual es útil para subidas idempotentes o para construir claves de búsqueda.

---

## `WithRandomSuffix()`

Agrega 8 caracteres hexadecimales aleatorios de un GUID nuevo.

```csharp
var ruta = StoragePath.From("foto.jpg").WithRandomSuffix();
// → StoragePath("foto_5f3a2b1c.jpg")
```

Usalo cuando necesitás unicidad garantizada sin la sobrecarga de verificar el storage en busca de conflictos. Cada llamada produce un sufijo diferente.

---

## `Sanitize()`

Normaliza una ruta a un subconjunto seguro de caracteres:

- Reemplaza barras invertidas (`\`) por barras hacia adelante (`/`)
- Colapsa barras consecutivas (`//`) en barras simples
- Reemplaza cualquier carácter que no sea alfanumérico, `-`, `_`, `.` o `/` por `_`
- Elimina barras iniciales y finales

```csharp
var ruta = StoragePath.From("Mis Documentos\\Reporte 2026!.pdf").Sanitize();
// → StoragePath("Mis_Documentos/Reporte_2026_.pdf")
```

Usalo cuando las rutas se construyen a partir de input del usuario (nombres de archivo, nombres de carpetas) para prevenir ataques de path traversal y problemas de codificación.

---

## Encadenamiento

Todos los métodos devuelven `StoragePath`, por lo que pueden encadenarse en cualquier combinación:

```csharp
// Sanear input del usuario, luego agregar prefijo de fecha para organización
var ruta = StoragePath.From(nombreArchivoDelUsuario)
    .Sanitize()
    .WithDatePrefix();
// → "2026/03/17/mi_archivo.pdf"

// Prefijo de fecha + sufijo aleatorio para archivo a prueba de colisiones
var rutaArchivo = StoragePath.From("backup.tar.gz")
    .WithDatePrefix()
    .WithRandomSuffix();
// → "2026/03/17/backup_c2a3f1b4.tar.gz"

// Prefijo de timestamp para trazabilidad de auditoría
var rutaAuditoria = StoragePath.From(nombreArchivo)
    .Sanitize()
    .WithTimestampPrefix();
// → "2026/03/17/09-15-00/factura_12345.pdf"
```

---

## Cuándo usar cada uno

| Escenario | Método recomendado |
|---|---|
| Organizar subidas por día, habilitar políticas de ciclo de vida en S3 | `WithDatePrefix()` |
| Agrupar archivos por el job o batch que los creó | `WithTimestampPrefix()` |
| Ruta determinista a partir de ID de usuario o huella de contenido | `WithHashSuffix(userId)` |
| Evitar colisiones de nombre sin consultar el storage | `WithRandomSuffix()` |
| Aceptar nombres de archivo proporcionados por usuarios de forma segura | `Sanitize()` |
| Aceptar y organizar nombres de archivo de usuarios | `.Sanitize().WithDatePrefix()` |

---

## Integración con `ConflictResolution`

Estos helpers funcionan bien junto a `ConflictResolution.Overwrite` cuando querés gestionar la unicidad vos mismo:

```csharp
var ruta = StoragePath.From(nombreArchivoSubido)
    .Sanitize()
    .WithHashSuffix(userId); // determinista por usuario y nombre de archivo

var request = new UploadRequest
{
    Path = ruta,
    Content = fileStream,
    ConflictResolution = ConflictResolution.Overwrite // idempotente — el mismo usuario re-subiendo el mismo nombre
};
```
