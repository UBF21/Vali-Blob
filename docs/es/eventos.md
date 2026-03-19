# Event Hooks

ValiBlob incluye un sistema de event hooks que permite ejecutar lógica personalizada cada vez que ocurre una operación de storage: uploads, downloads, deletes, y fallos. Es el mecanismo ideal para auditoría, métricas personalizadas, notificaciones y logging estructurado.

---

## Qué son los event hooks

Los event hooks son callbacks asíncronos que ValiBlob invoca automáticamente después de completar (o fallar) cada operación. Se implementan a través de la interfaz `IStorageEventHandler`.

A diferencia del pipeline de middleware (que transforma el request), los event handlers son observadores: reciben información sobre lo que ocurrió pero no modifican el resultado.

---

## Eventos disponibles

| Evento | Cuándo se dispara |
|---|---|
| `OnUploadCompletedAsync` | Después de un upload exitoso |
| `OnUploadFailedAsync` | Cuando un upload falla (incluyendo errores de validación) |
| `OnDownloadCompletedAsync` | Después de un download exitoso |
| `OnDeleteCompletedAsync` | Después de un delete (exitoso o fallido) |

---

## Referencia de `StorageEventContext`

Todos los eventos reciben un `StorageEventContext` con información completa sobre la operación:

| Propiedad | Tipo | Descripción |
|---|---|---|
| `ProviderName` | `string` | Nombre del proveedor (`"AWS"`, `"Azure"`, `"GCP"`, etc.) |
| `OperationType` | `string` | Tipo de operación (`"Upload"`, `"Download"`, `"Delete"`, etc.) |
| `Path` | `string?` | Ruta del archivo afectado |
| `IsSuccess` | `bool` | Si la operación fue exitosa |
| `ErrorMessage` | `string?` | Mensaje de error (si `IsSuccess = false`) |
| `ErrorCode` | `StorageErrorCode` | Código de error estructurado |
| `Duration` | `TimeSpan` | Tiempo que tomó la operación |
| `FileSizeBytes` | `long?` | Tamaño del archivo en bytes (disponible en uploads y downloads) |
| `Extra` | `IDictionary<string, object>` | Datos adicionales específicos del proveedor o del middleware |

---

## Implementar `IStorageEventHandler`

### Ejemplo completo: registro de auditoría

```csharp
using ValiBlob.Core.Events;

public sealed class AuditStorageEventHandler : IStorageEventHandler
{
    private readonly ILogger<AuditStorageEventHandler> _logger;
    private readonly IAuditRepository _auditRepo;

    public AuditStorageEventHandler(
        ILogger<AuditStorageEventHandler> logger,
        IAuditRepository auditRepo)
    {
        _logger = logger;
        _auditRepo = auditRepo;
    }

    public async Task OnUploadCompletedAsync(
        StorageEventContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Upload completado: {Path} en {Provider} ({Size} bytes, {Duration}ms)",
            context.Path,
            context.ProviderName,
            context.FileSizeBytes,
            context.Duration.TotalMilliseconds);

        await _auditRepo.RegistrarAsync(new AuditRecord
        {
            Operacion = "UPLOAD",
            Proveedor = context.ProviderName,
            Ruta = context.Path,
            TamanioBytes = context.FileSizeBytes,
            DuracionMs = (long)context.Duration.TotalMilliseconds,
            Exitoso = true,
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    public async Task OnUploadFailedAsync(
        StorageEventContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Upload fallido: {Path} en {Provider} — [{ErrorCode}] {Error}",
            context.Path,
            context.ProviderName,
            context.ErrorCode,
            context.ErrorMessage);

        await _auditRepo.RegistrarAsync(new AuditRecord
        {
            Operacion = "UPLOAD_FAILED",
            Proveedor = context.ProviderName,
            Ruta = context.Path,
            CodigoError = context.ErrorCode.ToString(),
            MensajeError = context.ErrorMessage,
            DuracionMs = (long)context.Duration.TotalMilliseconds,
            Exitoso = false,
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    public async Task OnDownloadCompletedAsync(
        StorageEventContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Download completado: {Path} en {Provider} ({Duration}ms)",
            context.Path,
            context.ProviderName,
            context.Duration.TotalMilliseconds);

        await _auditRepo.RegistrarAsync(new AuditRecord
        {
            Operacion = "DOWNLOAD",
            Proveedor = context.ProviderName,
            Ruta = context.Path,
            DuracionMs = (long)context.Duration.TotalMilliseconds,
            Exitoso = true,
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    public async Task OnDeleteCompletedAsync(
        StorageEventContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Delete completado: {Path} en {Provider} (Exitoso: {Success})",
            context.Path,
            context.ProviderName,
            context.IsSuccess);

        await _auditRepo.RegistrarAsync(new AuditRecord
        {
            Operacion = "DELETE",
            Proveedor = context.ProviderName,
            Ruta = context.Path,
            Exitoso = context.IsSuccess,
            MensajeError = context.IsSuccess ? null : context.ErrorMessage,
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);
    }
}
```

---

## Registrar handlers

### Método fluido (recomendado)

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithEventHandler<AuditStorageEventHandler>()
    .WithDefaultProvider("AWS");
```

### Dentro de `WithEventHandlers`

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithEventHandlers(services =>
    {
        services.AddSingleton<IStorageEventHandler, AuditStorageEventHandler>();
    })
    .WithDefaultProvider("AWS");
```

---

## Múltiples handlers

Podés registrar más de un handler. ValiBlob los invoca a todos en el orden de registro.

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithEventHandler<AuditStorageEventHandler>()    // registro de auditoría en DB
    .WithEventHandler<SlackNotificationHandler>()     // notificaciones a Slack
    .WithEventHandler<MetricsEventHandler>()          // métricas personalizadas
    .WithDefaultProvider("AWS");
```

### Ejemplo: handler de notificaciones

```csharp
public sealed class SlackNotificationHandler : IStorageEventHandler
{
    private readonly ISlackClient _slack;

    public SlackNotificationHandler(ISlackClient slack)
    {
        _slack = slack;
    }

    public Task OnUploadCompletedAsync(StorageEventContext context, CancellationToken ct = default)
        => Task.CompletedTask; // No notificar en uploads exitosos

    public async Task OnUploadFailedAsync(StorageEventContext context, CancellationToken ct = default)
    {
        // Notificar sólo fallas en producción
        await _slack.SendAsync(
            channel: "#alertas-storage",
            message: $":red_circle: Upload fallido en {context.ProviderName}\n" +
                     $"Ruta: `{context.Path}`\n" +
                     $"Error: {context.ErrorCode} — {context.ErrorMessage}");
    }

    public Task OnDownloadCompletedAsync(StorageEventContext context, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task OnDeleteCompletedAsync(StorageEventContext context, CancellationToken ct = default)
        => Task.CompletedTask;
}
```

---

## Manejo de errores en handlers

> **⚠️ Advertencia:** Las excepciones lanzadas en un `IStorageEventHandler` **nunca se propagan** a quien llamó la operación original. ValiBlob captura internamente cualquier excepción en los handlers y la registra como warning en el logger, para no impactar el flujo principal de la aplicación.

Esto significa que si tu handler falla (por ejemplo, la base de datos de auditoría no está disponible), la operación de upload/download/delete se considera igualmente exitosa.

Si necesitás garantías de entrega para tu auditoría, considerá un patrón de outbox: en lugar de escribir directamente a la DB, encolá el evento en una tabla de outbox y procésalo de forma asíncrona.

```csharp
public sealed class OutboxAuditHandler : IStorageEventHandler
{
    private readonly IOutboxQueue _outbox;

    public OutboxAuditHandler(IOutboxQueue outbox)
    {
        _outbox = outbox;
    }

    public async Task OnUploadCompletedAsync(StorageEventContext context, CancellationToken ct = default)
    {
        // Encolar en outbox — resiliente a fallos de la DB principal
        await _outbox.EnqueueAsync(new OutboxMessage
        {
            Type = "storage.upload.completed",
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                context.ProviderName,
                context.Path,
                context.FileSizeBytes,
                context.Duration,
                Timestamp = DateTimeOffset.UtcNow
            })
        }, ct);
    }

    public Task OnUploadFailedAsync(StorageEventContext context, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task OnDownloadCompletedAsync(StorageEventContext context, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task OnDeleteCompletedAsync(StorageEventContext context, CancellationToken ct = default)
        => Task.CompletedTask;
}
```
