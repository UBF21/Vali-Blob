# Almacenes de sesión para subidas reanudables

Las subidas reanudables requieren un store persistente para rastrear cada sesión de subida en progreso. Cuando un cliente reanuda tras un error de red, ValiBlob busca la sesión para saber cuántos bytes ya se recibieron y desde dónde continuar.

---

## Por qué el store en memoria no alcanza para producción

Por defecto, ValiBlob usa un store de sesiones en memoria. Esto funciona bien en desarrollo y en despliegues de instancia única con subidas cortas, pero tiene dos limitaciones importantes:

1. **Las sesiones se pierden al reiniciar.** Si el servidor se reinicia en medio de una subida, el cliente no puede reanudar — debe empezar de cero.
2. **Las sesiones no se comparten entre instancias.** En un despliegue con balanceo de carga, el cliente puede conectarse a un nodo diferente al reanudar, que no tiene conocimiento de la sesión iniciada por el primer nodo.

Para producción, reemplazá el store por defecto con `ValiBlob.Redis` o `ValiBlob.EntityFramework`.

---

## `ValiBlob.Redis`

Las sesiones se serializan como JSON y se almacenan en Redis con un TTL automático derivado del campo `ExpiresAt` de la sesión. El store es seguro para despliegues distribuidos y multi-instancia.

### Instalación

```bash
dotnet add package ValiBlob.Redis
```

### Configuración

```csharp
using ValiBlob.Redis.DependencyInjection;

// Opción A: proporcionar una cadena de conexión — ValiBlob crea el multiplexer
builder.Services.AddValiRedisSessionStore(
    connectionString: builder.Configuration["Redis:ConnectionString"]!,
    configure: opts =>
    {
        opts.KeyPrefix = "miapp"; // claves Redis: miapp:session:{uploadId}
    });

// Opción B: reutilizar un IConnectionMultiplexer ya registrado en el contenedor
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"]!));

builder.Services.AddValiRedisSessionStore(configure: opts =>
{
    opts.KeyPrefix = "miapp";
});
```

### Opciones

| Propiedad | Tipo | Valor por defecto | Descripción |
|---|---|---|---|
| `KeyPrefix` | `string` | `"valiblob"` | Prefijo para todas las claves Redis. Clave completa: `{KeyPrefix}:session:{uploadId}` |
| `ConfigurationString` | `string` | `"localhost:6379"` | Cadena de conexión usada al crear el multiplexer internamente |

### Comportamiento ante fallas de Redis

`GetAsync` trata un `RedisException` como un cache miss y devuelve `null` (degradación elegante). `SaveAsync` y `DeleteAsync` propagan los errores como `StorageException`.

---

## `ValiBlob.EntityFramework`

Las sesiones se persisten en una tabla de base de datos relacional (`ValiBlob_ResumableSessions`) vía Entity Framework Core. Cualquier base de datos soportada por EF Core funciona: SQL Server, PostgreSQL, SQLite, MySQL y otras.

### Instalación

```bash
dotnet add package ValiBlob.EntityFramework
```

### Configuración

#### `ValiResumableDbContext` independiente

El enfoque más simple: registrá el propio `DbContext` de ValiBlob y dejá que gestione la tabla de sesiones.

```csharp
using ValiBlob.EntityFramework.DependencyInjection;

builder.Services.AddValiEfCoreSessionStore(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
```

#### Integración con tu `DbContext` existente

Si preferís mantener todas las migraciones en tu propio `DbContext`, heredá de `ValiResumableDbContext`:

```csharp
public class AppDbContext : ValiResumableDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Orden> Ordenes => Set<Orden>();
    // ... tus propias entidades
}

// Registro
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddValiEfCoreSessionStore<AppDbContext>();
```

### Migraciones

Con el `ValiResumableDbContext` independiente:

```bash
dotnet ef migrations add AgregarSesionesReanudables \
    --context ValiResumableDbContext \
    --project src/TuProyecto

dotnet ef database update --context ValiResumableDbContext
```

Con tu propio contexto heredado:

```bash
dotnet ef migrations add AgregarSesionesReanudables --context AppDbContext
dotnet ef database update --context AppDbContext
```

### Esquema

El store EF Core crea una tabla:

```
ValiBlob_ResumableSessions
├─ UploadId        VARCHAR(128)  PK
├─ Path            VARCHAR(2048)
├─ BucketOverride  VARCHAR
├─ TotalSize       BIGINT
├─ BytesUploaded   BIGINT
├─ ContentType     VARCHAR
├─ CreatedAt       DATETIMEOFFSET
├─ ExpiresAt       DATETIMEOFFSET  (indexado)
├─ IsAborted       BIT
├─ IsComplete      BIT
├─ MetadataJson    TEXT
└─ ProviderDataJson TEXT
```

---

## Implementar un `IResumableSessionStore` personalizado

Si Redis y EF Core no se adaptan a tus requisitos, implementá la interfaz directamente:

```csharp
public interface IResumableSessionStore
{
    Task SaveAsync(ResumableUploadSession session, CancellationToken cancellationToken = default);
    Task<ResumableUploadSession?> GetAsync(string uploadId, CancellationToken cancellationToken = default);
    Task UpdateAsync(ResumableUploadSession session, CancellationToken cancellationToken = default);
    Task DeleteAsync(string uploadId, CancellationToken cancellationToken = default);
}
```

Registrá tu implementación:

```csharp
builder.Services.AddSingleton<IResumableSessionStore, MiStorePersonalizado>();
```

---

## Comparación

| Característica | InMemory (por defecto) | Redis | EF Core |
|---|---|---|---|
| Persistencia entre reinicios | No | Sí | Sí |
| Seguro para multi-instancia | No | Sí | Sí |
| Dependencia externa | Ninguna | Servidor Redis | Servidor de BD |
| Soporte de TTL para sesiones | No | Sí (automático) | Sí (limpieza manual) |
| Apto para producción | No | Sí | Sí |
| Package | incluido | `ValiBlob.Redis` | `ValiBlob.EntityFramework` |
