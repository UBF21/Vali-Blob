# Resumable Upload Session Stores

Resumable uploads require a persistent store to track each in-progress upload session. When a client resumes after a network failure, ValiBlob looks up the session to find out how many bytes have already been received and where to continue.

---

## Why the default in-memory store is not enough

By default, ValiBlob uses an in-memory session store. This works well in development and in single-instance deployments with short-lived uploads, but it has two significant limitations:

1. **Sessions are lost on restart.** If the server restarts mid-upload, the client cannot resume — it must start over.
2. **Sessions are not shared across instances.** In a load-balanced deployment, the client may connect to a different server node on resume, which has no knowledge of the session started by the first node.

For production, replace the default store with `ValiBlob.Redis` or `ValiBlob.EntityFramework`.

---

## `ValiBlob.Redis`

Sessions are serialized as JSON and stored in Redis with an automatic TTL derived from the session's `ExpiresAt` field. The store is safe for multi-instance and distributed deployments.

### Installation

```bash
dotnet add package ValiBlob.Redis
```

### Configuration

```csharp
using ValiBlob.Redis.DependencyInjection;

// Option A: provide a connection string — ValiBlob creates the multiplexer
builder.Services.AddValiRedisSessionStore(
    connectionString: builder.Configuration["Redis:ConnectionString"]!,
    configure: opts =>
    {
        opts.KeyPrefix = "myapp"; // Redis keys: myapp:session:{uploadId}
    });

// Option B: reuse an IConnectionMultiplexer already registered in the container
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"]!));

builder.Services.AddValiRedisSessionStore(configure: opts =>
{
    opts.KeyPrefix = "myapp";
});
```

### Options

| Property | Type | Default | Description |
|---|---|---|---|
| `KeyPrefix` | `string` | `"valiblob"` | Prefix for all Redis keys. Full key: `{KeyPrefix}:session:{uploadId}` |
| `ConfigurationString` | `string` | `"localhost:6379"` | Connection string used when creating the multiplexer internally |

### Behaviour on Redis failure

`GetAsync` treats a `RedisException` as a cache miss and returns `null` (graceful degradation). `SaveAsync` and `DeleteAsync` propagate errors as `StorageException`.

---

## `ValiBlob.EntityFramework`

Sessions are persisted to a relational database table (`ValiBlob_ResumableSessions`) via Entity Framework Core. Any EF Core-supported database works: SQL Server, PostgreSQL, SQLite, MySQL, and others.

> **Compatibility:** `ValiBlob.EFCore` targets **net8.0 and net9.0 only**. EF Core 9 dropped support for .NET 7 and earlier. If your project targets .NET 6 or 7, use `ValiBlob.Redis` instead.

### Installation

```bash
dotnet add package ValiBlob.EntityFramework
```

### Configuration

#### Standalone `ValiResumableDbContext`

The simplest approach: register ValiBlob's own `DbContext` and let it manage the sessions table.

```csharp
using ValiBlob.EntityFramework.DependencyInjection;

builder.Services.AddValiEfCoreSessionStore(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
```

#### Integrating with your existing `DbContext`

If you prefer to keep all migrations in your own `DbContext`, inherit from `ValiResumableDbContext`:

```csharp
public class AppDbContext : ValiResumableDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    // ... your own entities
}

// Register
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddValiEfCoreSessionStore<AppDbContext>();
```

### Migrations

When using the standalone `ValiResumableDbContext`, run:

```bash
dotnet ef migrations add AddValiResumableSessions \
    --context ValiResumableDbContext \
    --project src/YourProject

dotnet ef database update --context ValiResumableDbContext
```

When inheriting in your own context, standard migrations apply:

```bash
dotnet ef migrations add AddValiResumableSessions --context AppDbContext
dotnet ef database update --context AppDbContext
```

### Schema

The EF Core store creates one table:

```
ValiBlob_ResumableSessions
├─ UploadId        VARCHAR(128)  PK
├─ Path            VARCHAR(2048)
├─ BucketOverride  VARCHAR
├─ TotalSize       BIGINT
├─ BytesUploaded   BIGINT
├─ ContentType     VARCHAR
├─ CreatedAt       DATETIMEOFFSET
├─ ExpiresAt       DATETIMEOFFSET  (indexed)
├─ IsAborted       BIT
├─ IsComplete      BIT
├─ MetadataJson    TEXT
└─ ProviderDataJson TEXT
```

---

## Implementing a custom `IResumableSessionStore`

If Redis and EF Core do not fit your requirements, implement the interface directly:

```csharp
public interface IResumableSessionStore
{
    Task SaveAsync(ResumableUploadSession session, CancellationToken cancellationToken = default);
    Task<ResumableUploadSession?> GetAsync(string uploadId, CancellationToken cancellationToken = default);
    Task UpdateAsync(ResumableUploadSession session, CancellationToken cancellationToken = default);
    Task DeleteAsync(string uploadId, CancellationToken cancellationToken = default);
}
```

Register your implementation:

```csharp
builder.Services.AddSingleton<IResumableSessionStore, MyCustomSessionStore>();
```

---

## Comparison

| Feature | InMemory (default) | Redis | EF Core |
|---|---|---|---|
| Persistence across restarts | No | Yes | Yes |
| Multi-instance safe | No | Yes | Yes |
| External dependency | None | Redis server | Database server |
| Session TTL support | No | Yes (automatic) | Yes (manual cleanup) |
| Suitable for production | No | Yes | Yes |
| Package | built-in | `ValiBlob.Redis` | `ValiBlob.EntityFramework` |
| .NET compatibility | all TFMs | netstandard2.0+ / net6–9 | **net8.0 and net9.0 only** |
