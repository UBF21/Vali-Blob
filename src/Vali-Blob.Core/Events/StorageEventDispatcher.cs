using Microsoft.Extensions.Logging;

namespace ValiBlob.Core.Events;

public sealed class StorageEventDispatcher : IStorageEventDispatcher
{
    private readonly IEnumerable<IStorageEventHandler> _handlers;
    private readonly ILogger<StorageEventDispatcher> _logger;

    public StorageEventDispatcher(IEnumerable<IStorageEventHandler> handlers, ILogger<StorageEventDispatcher> logger)
    {
        _handlers = handlers;
        _logger = logger;
    }

    public Task DispatchUploadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
        => DispatchAsync(h => h.OnUploadCompletedAsync(context, cancellationToken), nameof(IStorageEventHandler.OnUploadCompletedAsync));

    public Task DispatchUploadFailedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
        => DispatchAsync(h => h.OnUploadFailedAsync(context, cancellationToken), nameof(IStorageEventHandler.OnUploadFailedAsync));

    public Task DispatchDownloadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
        => DispatchAsync(h => h.OnDownloadCompletedAsync(context, cancellationToken), nameof(IStorageEventHandler.OnDownloadCompletedAsync));

    public Task DispatchDeleteCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
        => DispatchAsync(h => h.OnDeleteCompletedAsync(context, cancellationToken), nameof(IStorageEventHandler.OnDeleteCompletedAsync));

    private Task DispatchAsync(Func<IStorageEventHandler, Task> dispatch, string eventName)
        => Task.WhenAll(_handlers.Select(h => InvokeHandlerAsync(h, dispatch, eventName)));

    private async Task InvokeHandlerAsync(IStorageEventHandler handler, Func<IStorageEventHandler, Task> dispatch, string eventName)
    {
        try
        {
            await dispatch(handler);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event handler {Handler} threw on {Event}", handler.GetType().Name, eventName);
        }
    }
}
