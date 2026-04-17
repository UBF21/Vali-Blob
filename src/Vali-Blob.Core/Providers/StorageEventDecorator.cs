using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Events;
using ValiBlob.Core.Models;

namespace ValiBlob.Core.Providers;

/// <summary>
/// Decorator that adds event dispatching to any IStorageProvider.
/// Fires storage events (Upload, Download, Delete) via a StorageEventDispatcher.
/// </summary>
public sealed class StorageEventDecorator : IStorageProvider
{
    private readonly IStorageProvider _inner;
    private readonly StorageEventDispatcher _dispatcher;

    public StorageEventDecorator(IStorageProvider inner, StorageEventDispatcher dispatcher)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public string ProviderName => _inner.ProviderName;

    public async Task<StorageResult<Stream>> DownloadAsync(
        DownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.DownloadAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await _dispatcher.DispatchDownloadCompletedAsync(new StorageEventContext
            {
                ProviderName = ProviderName,
                OperationType = "Download",
                Path = request.Path,
                IsSuccess = true
            }, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<StorageResult<UploadResult>> UploadAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.UploadAsync(request, progress, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await _dispatcher.DispatchUploadCompletedAsync(new StorageEventContext
            {
                ProviderName = ProviderName,
                OperationType = "Upload",
                Path = request.Path,
                IsSuccess = true
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _dispatcher.DispatchUploadFailedAsync(new StorageEventContext
            {
                ProviderName = ProviderName,
                OperationType = "Upload",
                Path = request.Path,
                IsSuccess = false,
                ErrorMessage = result.ErrorMessage
            }, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<StorageResult<bool>> ExistsAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        return await _inner.ExistsAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StorageResult<string>> GetUrlAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        return await _inner.GetUrlAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StorageResult> DeleteAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.DeleteAsync(path, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await _dispatcher.DispatchDeleteCompletedAsync(new StorageEventContext
            {
                ProviderName = ProviderName,
                OperationType = "Delete",
                Path = path,
                IsSuccess = true
            }, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesAsync(
        string? prefix = null,
        ListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await _inner.ListFilesAsync(prefix, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(
        IEnumerable<StoragePath> paths,
        CancellationToken cancellationToken = default)
    {
        return await _inner.DeleteManyAsync(paths, cancellationToken).ConfigureAwait(false);
    }

    public IAsyncEnumerable<FileEntry> ListAllAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.ListAllAsync(prefix, cancellationToken);
    }

    public async Task<StorageResult> DeleteFolderAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        return await _inner.DeleteFolderAsync(prefix, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        return await _inner.ListFoldersAsync(prefix, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StorageResult<FileMetadata>> GetMetadataAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        return await _inner.GetMetadataAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StorageResult> CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        return await _inner.CopyAsync(sourcePath, destinationPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StorageResult> MoveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        return await _inner.MoveAsync(sourcePath, destinationPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StorageResult<UploadResult>> UploadFromUrlAsync(
        string sourceUrl,
        StoragePath destinationPath,
        string? bucketOverride = null,
        CancellationToken cancellationToken = default)
    {
        return await _inner.UploadFromUrlAsync(sourceUrl, destinationPath, bucketOverride, cancellationToken).ConfigureAwait(false);
    }
}
