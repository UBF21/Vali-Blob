# Vali-Blob.EFCore

[![NuGet](https://img.shields.io/nuget/v/ValiBlob.EFCore.svg)](https://www.nuget.org/packages/ValiBlob.EFCore)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](https://github.com/UBF21/Vali-Blob/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209-purple)](https://www.nuget.org/packages/ValiBlob.EFCore)

Entity Framework Core session store for **Vali-Blob** resumable uploads.

Implements `IResumableSessionStore` backed by any EF Core-compatible relational database (SQL Server, PostgreSQL, SQLite, MySQL, and others), enabling durable, persistent tracking of resumable upload sessions across process restarts.

> **Compatibility:** This package targets **net8.0 and net9.0 only**. EF Core 9 requires .NET 8 or later. For projects targeting .NET 6 or 7, use [`ValiBlob.Redis`](https://www.nuget.org/packages/ValiBlob.Redis) instead.

---

## Compatibility

| Target Framework | Supported |
|---|---|
| `net8.0` | Yes |
| `net9.0` | Yes |
| `net6.0` / `net7.0` | No — use ValiBlob.Redis |

---

## Installation

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.EFCore

# Add your preferred EF Core database provider:
dotnet add package Microsoft.EntityFrameworkCore.SqlServer   # SQL Server
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL     # PostgreSQL
dotnet add package Microsoft.EntityFrameworkCore.Sqlite      # SQLite
dotnet add package Pomelo.EntityFrameworkCore.MySql          # MySQL / MariaDB
```

---

## Option A — Standalone `ValiResumableDbContext`

The simplest approach. Vali-Blob manages its own `DbContext` and migrations independently from your application context.

### Registration

```csharp
using ValiBlob.EFCore.DependencyInjection;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseAWS();

// SQL Server
builder.Services.AddValiEfCoreSessionStore(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// PostgreSQL
builder.Services.AddValiEfCoreSessionStore(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// SQLite
builder.Services.AddValiEfCoreSessionStore(opts =>
    opts.UseSqlite("Data Source=resumable.db"));
```

### Apply migrations

```bash
dotnet ef migrations add AddValiResumableSessions \
    --context ValiResumableDbContext \
    --project src/YourProject

dotnet ef database update --context ValiResumableDbContext
```

---

## Option B — Integrate with your existing `DbContext`

If you prefer to keep all migrations in your own context, inherit from `ValiResumableDbContext`:

```csharp
public class AppDbContext : ValiResumableDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Product> Products => Set<Product>();
    // ... your own entities
}
```

### Registration with existing context

```csharp
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddValiEfCoreSessionStore<AppDbContext>();
```

### Apply migrations

```bash
dotnet ef migrations add AddValiResumableSessions --context AppDbContext
dotnet ef database update --context AppDbContext
```

---

## Database schema

The store creates a single table `ValiBlob_ResumableSessions`:

```
ValiBlob_ResumableSessions
├── UploadId           VARCHAR(128)        PRIMARY KEY
├── Path               VARCHAR(2048)
├── BucketOverride     VARCHAR
├── TotalSize          BIGINT
├── BytesUploaded      BIGINT
├── ContentType        VARCHAR
├── CreatedAt          DATETIMEOFFSET
├── ExpiresAt          DATETIMEOFFSET      (indexed)
├── IsAborted          BIT
├── IsComplete         BIT
├── MetadataJson       TEXT
└── ProviderDataJson   TEXT
```

Expired sessions are **not** automatically deleted — add a background job or a database scheduled task to purge rows where `ExpiresAt < NOW()` and `IsComplete = true`.

---

## Usage with resumable uploads

```csharp
public class UploadController(IResumableUploadProvider resumable) : ControllerBase
{
    [HttpPost("start")]
    public async Task<IActionResult> StartAsync([FromBody] StartUploadDto dto)
    {
        var session = await resumable.StartUploadAsync(new ResumableUploadRequest
        {
            FileName    = dto.FileName,
            ContentType = dto.ContentType,
            TotalSize   = dto.TotalSize
        });

        // Session is now persisted to the database
        return Ok(new { sessionId = session.SessionId });
    }

    [HttpPost("{sessionId}/chunk/{index}")]
    public async Task<IActionResult> ChunkAsync(string sessionId, int index)
    {
        await resumable.UploadChunkAsync(new ResumableChunkRequest
        {
            SessionId  = sessionId,
            ChunkIndex = index,
            Data       = Request.Body
        });

        return NoContent();
    }

    [HttpPost("{sessionId}/complete")]
    public async Task<IActionResult> CompleteAsync(string sessionId)
    {
        var result = await resumable.CompleteUploadAsync(sessionId);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.ErrorMessage);
    }
}
```

---

## Comparison with other session stores

| Feature | InMemory (default) | ValiBlob.Redis | ValiBlob.EFCore |
|---|---|---|---|
| Persistence across restarts | No | Yes | Yes |
| Multi-instance / load-balanced | No | Yes | Yes |
| External dependency | None | Redis server | Database server |
| Automatic TTL expiry | No | Yes | No (manual cleanup) |
| .NET compatibility | all TFMs | net6–9 + netstandard | **net8 and net9 only** |
| Suitable for production | No | Yes | Yes |

---

## Features

| Feature | Supported |
|---|---|
| Standalone `ValiResumableDbContext` | Yes |
| Integration with existing `DbContext` | Yes |
| SQL Server, PostgreSQL, SQLite, MySQL | Yes |
| Indexed `ExpiresAt` for efficient cleanup | Yes |
| Scoped `IResumableSessionStore` registration | Yes |

---

## Documentation

- [Session stores docs](https://vali-blob-docs.netlify.app/docs/session-stores)
- [Resumable uploads](https://vali-blob-docs.netlify.app/docs/resumable-uploads)
- [Full documentation](https://vali-blob-docs.netlify.app)

---

## Links

- [GitHub Repository](https://github.com/UBF21/Vali-Blob)
- [NuGet Package](https://www.nuget.org/packages/ValiBlob.EFCore)

---

## Donations

If Vali-Blob is useful to you, consider supporting its development:

- **Latin America** — [MercadoPago](https://link.mercadopago.com.pe/felipermm)
- **International** — [PayPal](https://paypal.me/felipeRMM?country.x=PE&locale.x=es_XC)

---

## License

[MIT License](https://github.com/UBF21/Vali-Blob/blob/main/LICENSE)

## Contributions

Issues and pull requests are welcome on [GitHub](https://github.com/UBF21/Vali-Blob).
