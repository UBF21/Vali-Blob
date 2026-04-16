namespace ValiBlob.Core.Events;

/// <summary>Base class for storage event handlers with empty default implementations.</summary>
public abstract class StorageEventHandlerBase : IStorageEventHandler
{
    public virtual Task OnUploadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public virtual Task OnUploadFailedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public virtual Task OnDownloadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public virtual Task OnDeleteCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
