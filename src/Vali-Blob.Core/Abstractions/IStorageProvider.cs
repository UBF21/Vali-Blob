using System.Collections.Generic;
using ValiBlob.Core.Models;

namespace ValiBlob.Core.Abstractions;

public interface IStorageProvider
{
    string ProviderName { get; }

    Task<StorageResult<UploadResult>> UploadAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<StorageResult<Stream>> DownloadAsync(
        DownloadRequest request,
        CancellationToken cancellationToken = default);

    Task<StorageResult> DeleteAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<StorageResult<bool>> ExistsAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<StorageResult<string>> GetUrlAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<StorageResult> CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task<StorageResult> MoveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task<StorageResult<FileMetadata>> GetMetadataAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<StorageResult> SetMetadataAsync(
        string path,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesAsync(
        string? prefix = null,
        ListOptions? options = null,
        CancellationToken cancellationToken = default);

    // --- Batch operations ---

    Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(
        IEnumerable<StoragePath> paths,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<FileEntry> ListAllAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default);

    // --- Folder operations ---

    Task<StorageResult> DeleteFolderAsync(
        string prefix,
        CancellationToken cancellationToken = default);

    Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default);

    // --- Remote upload ---

    Task<StorageResult<UploadResult>> UploadFromUrlAsync(
        string sourceUrl,
        StoragePath destinationPath,
        string? bucketOverride = null,
        CancellationToken cancellationToken = default);
}
