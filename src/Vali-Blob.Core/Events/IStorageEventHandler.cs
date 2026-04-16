namespace ValiBlob.Core.Events;

public interface IStorageEventHandler
{
    Task OnUploadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default);
    Task OnUploadFailedAsync(StorageEventContext context, CancellationToken cancellationToken = default);
    Task OnDownloadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default);
    Task OnDeleteCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default);
}
