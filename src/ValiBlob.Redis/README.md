# ValiBlob.Redis

Redis-backed resumable upload session store for [ValiBlob](https://github.com/valiblob/valiblob).

The default `InMemoryResumableSessionStore` loses all upload sessions on process restart. This package replaces it with a Redis store that survives restarts and works correctly across multiple application instances.

## Installation

```
dotnet add package ValiBlob.Redis
```

## Quick Start

```csharp
// Program.cs / Startup.cs
builder.Services.AddValiStorage(/* ... */);

// Replace the default in-memory store with Redis
builder.Services.AddValiRedisSessionStore(
    connectionString: "localhost:6379",
    configure: opts =>
    {
        opts.KeyPrefix = "myapp";   // optional — default is "valiblob"
    });
```

Sessions are stored as JSON under the key `{KeyPrefix}:session:{uploadId}` and automatically expire via Redis TTL when `ExpiresAt` is set.

## Using an existing IConnectionMultiplexer

If you already register `IConnectionMultiplexer` elsewhere, use the overload that does not create a new connection:

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));

builder.Services.AddValiRedisSessionStore(opts => opts.KeyPrefix = "myapp");
```
