# StoragePath

`StoragePath` es el tipo que ValiBlob usa para representar rutas de archivos en el storage. Reemplaza el uso de strings crudos y elimina toda una categoría de bugs.

---

## El problema con strings crudos

Cuando las rutas son simples `string`, estos bugs son posibles y difíciles de detectar:

```csharp
// Bug 1: doble slash
string ruta = "documentos/" + "/facturas/2024/" + "archivo.pdf";
// resultado: "documentos//facturas/2024/archivo.pdf"

// Bug 2: slash al final vs sin slash — se comportan diferente en S3
string ruta1 = "facturas/2024";
string ruta2 = "facturas/2024/";

// Bug 3: separador incorrecto (backslash de Windows)
string ruta = Path.Combine("documentos", "facturas"); // en Windows: "documentos\facturas"

// Bug 4: mayúsculas y minúsculas inconsistentes
string subida = "Documentos/Factura.pdf";
string descarga = "documentos/factura.pdf"; // no encuentra el archivo
```

`StoragePath` resuelve estos problemas al momento de la compilación y en tiempo de ejecución:

```csharp
// StoragePath normaliza automáticamente
var ruta = StoragePath.From("documentos", "/facturas/2024/", "archivo.pdf");
// resultado: "documentos/facturas/2024/archivo.pdf" — correcto siempre
```

---

## Métodos de creación

### `StoragePath.From(params string[] segments)`

El método principal. Acepta uno o más segmentos, los limpia y los une con `/`.

```csharp
// Un segmento
var ruta1 = StoragePath.From("archivo.pdf");
// "archivo.pdf"

// Múltiples segmentos
var ruta2 = StoragePath.From("documentos", "facturas", "2024", "enero.pdf");
// "documentos/facturas/2024/enero.pdf"

// Con slashes embebidos (se normalizan)
var ruta3 = StoragePath.From("documentos/facturas", "2024/enero.pdf");
// "documentos/facturas/2024/enero.pdf"

// Con slashes extra y espacios (se limpian)
var ruta4 = StoragePath.From("documentos/", " /facturas/ ", "archivo.pdf");
// "documentos/facturas/archivo.pdf"
```

> **💡 Tip:** `StoragePath.From` acepta strings con slashes embebidos. Esto es útil cuando tenés un path ya formado que querés pasar como valor tipado.

### Conversión implícita desde `string`

Podés asignar un `string` directamente donde se espera un `StoragePath`:

```csharp
StoragePath ruta = "documentos/facturas/enero.pdf";
// equivalente a StoragePath.From("documentos/facturas/enero.pdf")
```

---

## Operador `/`

El operador `/` permite construir rutas de manera fluida y segura:

```csharp
var base_ = StoragePath.From("documentos");
var ruta = base_ / "facturas" / "2024" / "enero.pdf";
// "documentos/facturas/2024/enero.pdf"

// Útil con variables
string tenantId = "tenant-123";
string año = DateTime.UtcNow.Year.ToString();
var ruta = StoragePath.From("tenants") / tenantId / "archivos" / año / "reporte.pdf";
// "tenants/tenant-123/archivos/2024/reporte.pdf"
```

### `Append(string segment)`

Método equivalente al operador `/`:

```csharp
var ruta = StoragePath.From("documentos").Append("facturas").Append("enero.pdf");
// "documentos/facturas/enero.pdf"
```

---

## Propiedades

### `FileName`

El último segmento de la ruta — el nombre del archivo.

```csharp
var ruta = StoragePath.From("documentos", "facturas", "enero.pdf");
Console.WriteLine(ruta.FileName); // "enero.pdf"
```

### `Extension`

La extensión del archivo incluyendo el punto. Retorna `null` si no hay extensión.

```csharp
var ruta = StoragePath.From("documentos", "enero.pdf");
Console.WriteLine(ruta.Extension); // ".pdf"

var sinExtension = StoragePath.From("documentos", "readme");
Console.WriteLine(sinExtension.Extension); // null
```

### `Parent`

Retorna una nueva `StoragePath` con todos los segmentos excepto el último. Retorna `null` si la ruta tiene un solo segmento.

```csharp
var ruta = StoragePath.From("documentos", "facturas", "enero.pdf");
Console.WriteLine(ruta.Parent);         // "documentos/facturas"
Console.WriteLine(ruta.Parent!.Parent); // "documentos"
Console.WriteLine(ruta.Parent!.Parent!.Parent); // null
```

### `Segments`

La colección inmutable de segmentos que conforman la ruta.

```csharp
var ruta = StoragePath.From("documentos", "facturas", "enero.pdf");
foreach (var segmento in ruta.Segments)
    Console.WriteLine(segmento);
// documentos
// facturas
// enero.pdf
```

---

## Conversiones implícitas

### De `StoragePath` a `string`

En cualquier lugar donde se espera un `string` (por ejemplo, los métodos de `IStorageProvider` que aceptan `string path`), podés pasar un `StoragePath` directamente:

```csharp
var ruta = StoragePath.From("documentos", "enero.pdf");

// Los métodos que aceptan string path reciben StoragePath sin cast
var result = await _storage.DeleteAsync(ruta);
var exists = await _storage.ExistsAsync(ruta);
var url = await _storage.GetUrlAsync(ruta);

// También funciona con string explícito
string comoString = ruta;
Console.WriteLine(comoString); // "documentos/enero.pdf"
```

### De `string` a `StoragePath`

```csharp
// Asignación implícita
StoragePath ruta = "documentos/enero.pdf";

// En parámetros de método
void ProcesarRuta(StoragePath ruta) { /* ... */ }
ProcesarRuta("documentos/enero.pdf"); // funciona
```

---

## Igualdad

`StoragePath` implementa igualdad por valor (compara los segmentos, no la referencia de objeto). La comparación es case-sensitive (igual que los proveedores cloud).

```csharp
var a = StoragePath.From("docs", "file.pdf");
var b = StoragePath.From("docs", "file.pdf");
var c = StoragePath.From("docs", "other.pdf");
var d = StoragePath.From("Docs", "file.pdf"); // distinto — mayúscula

Console.WriteLine(a == b);  // true
Console.WriteLine(a == c);  // false
Console.WriteLine(a == d);  // false — case-sensitive
Console.WriteLine(a.Equals(b)); // true

// Funciona en colecciones
var set = new HashSet<StoragePath> { a, b };
Console.WriteLine(set.Count); // 1 — son iguales

var dict = new Dictionary<StoragePath, string> { [a] = "valor" };
Console.WriteLine(dict[b]); // "valor" — b es igual a a
```

---

## Ejemplos del mundo real

### Documentos por tipo y fecha

```csharp
public static class RutasDocumentos
{
    public static StoragePath Factura(int año, int mes, string id) =>
        StoragePath.From("facturas", año.ToString(), mes.ToString("D2"), $"{id}.pdf");

    public static StoragePath Contrato(string clienteId, string version) =>
        StoragePath.From("contratos", clienteId, $"contrato-{version}.pdf");

    public static StoragePath ImagenProducto(string productoId, string variante) =>
        StoragePath.From("productos", productoId, "imagenes", $"{variante}.jpg");
}

// Uso
var ruta = RutasDocumentos.Factura(2024, 1, "FAC-00123");
// "facturas/2024/01/FAC-00123.pdf"
```

### Aislamiento por tenant (multi-tenant)

```csharp
public static class RutasTenant
{
    public static StoragePath Archivo(string tenantId, string categoria, string nombre) =>
        StoragePath.From("tenants") / tenantId / categoria / nombre;
}

// Uso
var tenantId = "acme-corp";
var ruta = RutasTenant.Archivo(tenantId, "reportes", "ventas-q1.xlsx");
// "tenants/acme-corp/reportes/ventas-q1.xlsx"
```

### Rutas dinámicas por fecha

```csharp
public static StoragePath PorFecha(string categoria, string nombreArchivo)
{
    var hoy = DateTime.UtcNow;
    return StoragePath.From(
        categoria,
        hoy.Year.ToString(),
        hoy.Month.ToString("D2"),
        hoy.Day.ToString("D2"),
        nombreArchivo
    );
}

// Uso
var ruta = PorFecha("logs", "app.log");
// "logs/2024/03/15/app.log"
```

### Copias y versiones

```csharp
// Crear ruta de backup basada en original
var original = StoragePath.From("documentos", "contrato.pdf");
var backup = original.Parent! / $"{Path.GetFileNameWithoutExtension(original.FileName)}-backup{original.Extension}";
// "documentos/contrato-backup.pdf"
```

---

## Anti-patrones a evitar

### No construyas rutas con concatenación de strings

```csharp
// MAL — propenso a errores de slashes
string ruta = "documentos/" + tenantId + "/" + año + "/" + archivo;

// BIEN
var ruta = StoragePath.From("documentos", tenantId, año.ToString(), archivo);
```

### No uses `Path.Combine` para rutas de storage

```csharp
// MAL — Path.Combine usa separadores del sistema operativo
string ruta = Path.Combine("documentos", "facturas", "archivo.pdf");
// En Windows: "documentos\facturas\archivo.pdf" — incorrecto para cloud

// BIEN
var ruta = StoragePath.From("documentos", "facturas", "archivo.pdf");
```

### No ignores la sensibilidad a mayúsculas

```csharp
// MAL — estas son dos rutas distintas en todos los proveedores cloud
await _storage.UploadAsync(new UploadRequest { Path = StoragePath.From("Documentos", "Archivo.PDF") });
await _storage.DownloadAsync(new DownloadRequest { Path = StoragePath.From("documentos", "archivo.pdf") });

// BIEN — establece una convención y cúmplela siempre (recomendado: todo en minúsculas)
var ruta = StoragePath.From("documentos", "archivo.pdf");
```

### No construyas rutas en partes de la aplicación y las ensamblas después

```csharp
// MAL — la ruta se fragmenta y pierde visibilidad
string prefijo = "documentos/" + tenantId;
string nombreArchivo = DateTime.Now.Ticks + ".pdf";
string rutaFinal = prefijo + "/" + nombreArchivo;

// BIEN
var ruta = StoragePath.From("documentos", tenantId, $"{DateTime.UtcNow.Ticks}.pdf");
```
