using ValiBlob.Core.Models;

namespace ValiBlob.Core.Abstractions;

public interface IPresignedUrlProvider
{
    Task<StorageResult<string>> GetPresignedUploadUrlAsync(
        string path,
        TimeSpan expiration,
        CancellationToken cancellationToken = default);

    Task<StorageResult<string>> GetPresignedDownloadUrlAsync(
        string path,
        TimeSpan expiration,
        CancellationToken cancellationToken = default);
}
