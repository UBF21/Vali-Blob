namespace ValiBlob.Core.Events;

public interface IStorageEventDispatcher
{
    Task DispatchUploadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default);
    Task DispatchUploadFailedAsync(StorageEventContext context, CancellationToken cancellationToken = default);
    Task DispatchDownloadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default);
    Task DispatchDeleteCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default);
}
