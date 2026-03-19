# Event Hooks

Event hooks allow you to run custom logic in response to storage lifecycle events — after an upload completes or fails, after a download completes, or after a delete completes. Common uses include audit logging, notification sending, analytics tracking, and cache invalidation.

---

## Available events

| Method | When it fires |
|---|---|
| `OnUploadCompletedAsync` | After a successful upload |
| `OnUploadFailedAsync` | After an upload that resulted in an error |
| `OnDownloadCompletedAsync` | After a successful download |
| `OnDeleteCompletedAsync` | After a delete operation (regardless of whether the file existed) |

---

## `StorageEventContext` properties

Every handler receives a `StorageEventContext` containing details about the operation.

| Property | Type | Description |
|---|---|---|
| `ProviderName` | `string` | Name of the storage provider (e.g., `"AWS"`, `"Azure"`) |
| `OperationType` | `string` | Operation name: `"Upload"`, `"Download"`, `"Delete"`, etc. |
| `Path` | `string?` | Object path that was affected |
| `IsSuccess` | `bool` | Whether the operation succeeded |
| `ErrorMessage` | `string?` | Error message if `IsSuccess` is `false` |
| `ErrorCode` | `StorageErrorCode` | Structured error code if `IsSuccess` is `false` |
| `Duration` | `TimeSpan` | How long the operation took |
| `FileSizeBytes` | `long?` | File size in bytes (available for upload/download events) |
| `Extra` | `IDictionary<string, object>` | Provider-specific or operation-specific extra data |

---

## Implementing `IStorageEventHandler`

Implement the `IStorageEventHandler` interface. You must implement all four methods; use a no-op body for events you don't care about.

### Audit log example

```csharp
using ValiBlob.Core.Events;

public sealed class StorageAuditHandler : IStorageEventHandler
{
    private readonly ILogger<StorageAuditHandler> _logger;
    private readonly IAuditService _auditService;

    public StorageAuditHandler(
        ILogger<StorageAuditHandler> logger,
        IAuditService auditService)
    {
        _logger = logger;
        _auditService = auditService;
    }

    public async Task OnUploadCompletedAsync(
        StorageEventContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "File uploaded: {Path} ({SizeBytes} bytes) via {Provider} in {Duration}ms",
            context.Path,
            context.FileSizeBytes,
            context.ProviderName,
            context.Duration.TotalMilliseconds);

        await _auditService.RecordAsync(new AuditEntry
        {
            EventType = "StorageUpload",
            ResourcePath = context.Path,
            Provider = context.ProviderName,
            SizeBytes = context.FileSizeBytes,
            DurationMs = (long)context.Duration.TotalMilliseconds,
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    public async Task OnUploadFailedAsync(
        StorageEventContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogError(
            "Upload failed: {Path} [{ErrorCode}] {ErrorMessage} via {Provider}",
            context.Path,
            context.ErrorCode,
            context.ErrorMessage,
            context.ProviderName);

        await _auditService.RecordAsync(new AuditEntry
        {
            EventType = "StorageUploadFailed",
            ResourcePath = context.Path,
            Provider = context.ProviderName,
            ErrorCode = context.ErrorCode.ToString(),
            ErrorMessage = context.ErrorMessage,
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    public async Task OnDownloadCompletedAsync(
        StorageEventContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "File downloaded: {Path} via {Provider} in {Duration}ms",
            context.Path,
            context.ProviderName,
            context.Duration.TotalMilliseconds);

        await _auditService.RecordAsync(new AuditEntry
        {
            EventType = "StorageDownload",
            ResourcePath = context.Path,
            Provider = context.ProviderName,
            DurationMs = (long)context.Duration.TotalMilliseconds,
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    public async Task OnDeleteCompletedAsync(
        StorageEventContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "File deleted: {Path} via {Provider}",
            context.Path,
            context.ProviderName);

        await _auditService.RecordAsync(new AuditEntry
        {
            EventType = "StorageDelete",
            ResourcePath = context.Path,
            Provider = context.ProviderName,
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);
    }
}
```

---

## Registering handlers

Use `WithEventHandler<T>()` on the `ValiStorageBuilder`:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithEventHandler<StorageAuditHandler>();
```

Or register directly on the service collection:

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithEventHandlers(services =>
    {
        services.AddSingleton<IStorageEventHandler, StorageAuditHandler>();
    });
```

---

## Multiple handlers

You can register as many handlers as needed. All handlers are resolved from the DI container as `IEnumerable<IStorageEventHandler>` and invoked in registration order.

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithEventHandler<StorageAuditHandler>()
    .WithEventHandler<CacheInvalidationHandler>()
    .WithEventHandler<SlackNotificationHandler>();
```

Each handler is called independently. A failure in one handler does not prevent the others from running.

### Cache invalidation example

```csharp
public sealed class CacheInvalidationHandler : IStorageEventHandler
{
    private readonly IMemoryCache _cache;

    public CacheInvalidationHandler(IMemoryCache cache) => _cache = cache;

    public Task OnUploadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
    {
        if (context.Path is not null)
            _cache.Remove($"file-metadata:{context.Path}");
        return Task.CompletedTask;
    }

    public Task OnUploadFailedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task OnDownloadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task OnDeleteCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
    {
        if (context.Path is not null)
            _cache.Remove($"file-metadata:{context.Path}");
        return Task.CompletedTask;
    }
}
```

---

## Error handling in handlers

Event handlers **never propagate exceptions to the caller**. The `StorageEventDispatcher` catches all exceptions from handlers, logs them as warnings, and continues. This means:

- A buggy or failing event handler cannot break a storage upload
- If `OnUploadCompletedAsync` throws, the upload result returned to the caller is still a success
- Audit logging failures are silently swallowed (they appear in application logs but are not surfaced to the caller)

If you need guaranteed delivery for critical events (e.g., billing records), use a reliable message queue (Azure Service Bus, AWS SQS) inside your handler rather than relying on in-process event delivery.

> **💡 Tip:** Always observe the `ILogger` output for handler errors during development. Since exceptions are swallowed, a misconfigured handler may silently do nothing without obvious indication.
