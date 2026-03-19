# ValiBlob.EntityFramework

Entity Framework Core session store for [ValiBlob](https://github.com/valiblob/valiblob) resumable uploads.

The default `InMemoryResumableSessionStore` loses all upload sessions on process restart. This package persists sessions to any EF Core-supported relational database (SQL Server, PostgreSQL, SQLite, MySQL, etc.).

## Installation

```
dotnet add package ValiBlob.EntityFramework
dotnet add package Microsoft.EntityFrameworkCore.SqlServer  # or your preferred provider
```

## Quick Start

```csharp
// Program.cs / Startup.cs
builder.Services.AddValiStorage(/* ... */);

// Register the EF Core session store (SQL Server example)
builder.Services.AddValiEfCoreSessionStore(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
```

This registers `ValiResumableDbContext` and `EfCoreResumableSessionStore` as the scoped `IResumableSessionStore`.

## Apply Migrations

```bash
dotnet ef migrations add AddValiResumableSessions --context ValiResumableDbContext
dotnet ef database update --context ValiResumableDbContext
```

## Using an Existing DbContext

If you prefer to add the table to your own context, inherit from `ValiResumableDbContext`:

```csharp
public class AppDbContext : ValiResumableDbContext
{
    public AppDbContext(DbContextOptions<ValiResumableDbContext> options) : base(options) { }
    // ... your own DbSets
}
```

Then register using the generic overload:

```csharp
builder.Services.AddDbContext<AppDbContext>(opts => opts.UseSqlServer(connStr));
builder.Services.AddValiEfCoreSessionStore<AppDbContext>();
```
