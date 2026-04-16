# Vali-Blob.Redis

[![NuGet](https://img.shields.io/nuget/v/ValiBlob.Redis.svg)](https://www.nuget.org/packages/ValiBlob.Redis)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](https://github.com/UBF21/Vali-Blob/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6%20%7C%207%20%7C%208%20%7C%209-purple)](https://www.nuget.org/packages/ValiBlob.Redis)

Redis-backed resumable upload session store for **Vali-Blob** — the unified cloud storage abstraction library for .NET.

The default `InMemoryResumableSessionStore` loses all upload sessions on process restart and cannot share state across multiple application instances. This package replaces it with a Redis-backed store backed by [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) that survives restarts and works correctly in horizontally scaled deployments.

---

## Compatibility

| Target Framework | Supported |
|---|---|
| `netstandard2.0` | Yes |
| `netstandard2.1` | Yes |
| `net6.0` | Yes |
| `net7.0` | Yes |
| `net8.0` | Yes |
| `net9.0` | Yes |

---

## Installation

```bash
dotnet add package ValiBlob.Core
dotnet add package ValiBlob.Redis
```

---

## Configuration

### `appsettings.json`

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379"
  },
  "ValiBlob:Redis": {
    "KeyPrefix": "myapp"
  }
}
```

---

## Registration

### Option A — let Vali-Blob create the connection

```csharp
using ValiBlob.Redis.DependencyInjection;

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseAWS();

builder.Services.AddValiRedisSessionStore(
    connectionString: builder.Configuration["Redis:ConnectionString"]!,
    configure: opts =>
    {
        opts.KeyPrefix = "myapp"; // Redis keys: myapp:session:{uploadId}
    });
```

### Option B — reuse an existing `IConnectionMultiplexer`

```csharp
// Register multiplexer once (shared with other Redis clients in your app)
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"]!));

builder.Services
    .AddValiBlob(opts => opts.DefaultProvider = "AWS")
    .UseAWS();

builder.Services.AddValiRedisSessionStore(configure: opts =>
{
    opts.KeyPrefix = "myapp";
});
```

---

## How it works

Each upload session is serialized as JSON and stored in Redis under the key:

```
{KeyPrefix}:session:{uploadId}
```

The TTL is set automatically to match the session's `ExpiresAt` field — expired sessions are cleaned up by Redis with no manual intervention.

```
myapp:session:a1b2c3d4-...  →  { "uploadId": "...", "path": "...", "bytesUploaded": 10485760, ... }
```

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

        // Session is now stored in Redis — survives restarts and load balancer hops
        return Ok(new { sessionId = session.SessionId });
    }

    [HttpPost("{sessionId}/chunk/{index}")]
    public async Task<IActionResult> UploadChunkAsync(string sessionId, int index)
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

## Behaviour on Redis failure

| Operation | Behaviour on failure |
|---|---|
| `GetAsync` | Returns `null` (graceful cache miss — upload can restart) |
| `SaveAsync` | Propagates `StorageException` |
| `UpdateAsync` | Propagates `StorageException` |
| `DeleteAsync` | Propagates `StorageException` |

---

## Features

| Feature | Supported |
|---|---|
| Persistence across process restarts | Yes |
| Multi-instance / load-balanced safe | Yes |
| Automatic TTL-based expiry | Yes |
| Configurable key prefix | Yes |
| Reuse existing `IConnectionMultiplexer` | Yes |
| Graceful degradation on Redis failure | Yes (`GetAsync`) |

---

## Options reference

| Property | Default | Description |
|---|---|---|
| `KeyPrefix` | `"valiblob"` | Prefix for all Redis keys. Full key: `{KeyPrefix}:session:{uploadId}` |
| `ConfigurationString` | `"localhost:6379"` | Connection string used when creating the multiplexer internally |

---

## Documentation

- [Session stores docs](https://vali-blob-docs.netlify.app/docs/session-stores)
- [Resumable uploads](https://vali-blob-docs.netlify.app/docs/resumable-uploads)
- [Full documentation](https://vali-blob-docs.netlify.app)

---

## Links

- [GitHub Repository](https://github.com/UBF21/Vali-Blob)
- [NuGet Package](https://www.nuget.org/packages/ValiBlob.Redis)

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
