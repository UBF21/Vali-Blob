using Microsoft.Extensions.Logging;

namespace ValiBlob.Core.Events;

public sealed class StorageEventDispatcher
{
    private readonly IEnumerable<IStorageEventHandler> _handlers;
    private readonly ILogger<StorageEventDispatcher> _logger;

    public StorageEventDispatcher(IEnumerable<IStorageEventHandler> handlers, ILogger<StorageEventDispatcher> logger)
    {
        _handlers = handlers;
        _logger = logger;
    }

    public async Task DispatchUploadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
        => await DispatchAsync(h => h.OnUploadCompletedAsync(context, cancellationToken), nameof(IStorageEventHandler.OnUploadCompletedAsync));

    public async Task DispatchUploadFailedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
        => await DispatchAsync(h => h.OnUploadFailedAsync(context, cancellationToken), nameof(IStorageEventHandler.OnUploadFailedAsync));

    public async Task DispatchDownloadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
        => await DispatchAsync(h => h.OnDownloadCompletedAsync(context, cancellationToken), nameof(IStorageEventHandler.OnDownloadCompletedAsync));

    public async Task DispatchDeleteCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
        => await DispatchAsync(h => h.OnDeleteCompletedAsync(context, cancellationToken), nameof(IStorageEventHandler.OnDeleteCompletedAsync));

    private async Task DispatchAsync(Func<IStorageEventHandler, Task> dispatch, string eventName)
    {
        foreach (var handler in _handlers)
        {
            try
            {
                await dispatch(handler);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Event handler {Handler} threw on {Event}", handler.GetType().Name, eventName);
                // Never let event handler failures propagate — they are fire-and-observe
            }
        }
    }
}
