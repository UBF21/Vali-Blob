using System.Collections.Generic;
using ValiBlob.Core.Models;

namespace ValiBlob.Core.Abstractions;

/// <summary>Core read operations: download, exists, get URL.</summary>
public interface IStorageReader
{
    Task<StorageResult<Stream>> DownloadAsync(
        DownloadRequest request,
        CancellationToken cancellationToken = default);

    Task<StorageResult<bool>> ExistsAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<StorageResult<string>> GetUrlAsync(
        string path,
        CancellationToken cancellationToken = default);
}

/// <summary>Core write operations: upload, delete.</summary>
public interface IStorageWriter
{
    Task<StorageResult<UploadResult>> UploadAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<StorageResult> DeleteAsync(
        string path,
        CancellationToken cancellationToken = default);
}

/// <summary>Listing and batch delete operations.</summary>
public interface IStorageLister
{
    Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesAsync(
        string? prefix = null,
        ListOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(
        IEnumerable<StoragePath> paths,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<FileEntry> ListAllAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default);

    Task<StorageResult> DeleteFolderAsync(
        string prefix,
        CancellationToken cancellationToken = default);

    Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Metadata operations. Not all providers support SetMetadata.</summary>
public interface IStorageMetadata
{
    Task<StorageResult<FileMetadata>> GetMetadataAsync(
        string path,
        CancellationToken cancellationToken = default);
}

/// <summary>Optional: metadata write support. Not all providers implement this.</summary>
public interface IMetadataWritableProvider : IStorageMetadata
{
    Task<StorageResult> SetMetadataAsync(
        string path,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>Remote and copy/move operations.</summary>
public interface IStorageRemote
{
    Task<StorageResult> CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task<StorageResult> MoveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task<StorageResult<UploadResult>> UploadFromUrlAsync(
        string sourceUrl,
        StoragePath destinationPath,
        string? bucketOverride = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Core storage provider combining all basic operations.
/// Implements all segregated interfaces for backwards compatibility.
/// Providers that don't support SetMetadata should still implement this interface
/// and return NotSupported for SetMetadataAsync, or prefer IMetadataWritableProvider.
/// </summary>
public interface IStorageProvider : IStorageReader, IStorageWriter, IStorageLister, IStorageMetadata, IStorageRemote
{
    string ProviderName { get; }
}
