using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Events;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Resilience;
using ValiBlob.Core.Telemetry;

namespace ValiBlob.Core.Providers;

public abstract class BaseStorageProvider : IStorageProvider
{
    protected readonly ILogger Logger;
    private readonly ResilienceOptions _resilienceOptions;
    private readonly StoragePipelineBuilder _pipeline;
    private readonly Lazy<ResiliencePipeline> _lazyResiliencePipeline;
    private readonly EncryptionOptions _encryptionOptions;
    private readonly Func<string, HttpClient>? _httpClientFactory;
    private readonly DownloadTransformPipeline _downloadTransforms;
    private StorageEventDispatcher? _eventDispatcher;

    protected BaseStorageProvider(
        ILogger logger,
        IOptions<ResilienceOptions> resilienceOptions,
        IOptions<EncryptionOptions> encryptionOptions,
        StoragePipelineBuilder pipeline,
        Func<string, HttpClient>? httpClientFactory = null)
    {
        Logger = logger;
        _resilienceOptions = resilienceOptions.Value;
        _encryptionOptions = encryptionOptions.Value;
        _pipeline = pipeline;
        _httpClientFactory = httpClientFactory;
        _downloadTransforms = new DownloadTransformPipeline(_encryptionOptions, this);
        _lazyResiliencePipeline = new Lazy<ResiliencePipeline>(() =>
            ResiliencePipelineFactory.BuildPipeline(_resilienceOptions, Logger, ProviderName));
    }

    internal void SetEventDispatcher(StorageEventDispatcher dispatcher)
    {
        _eventDispatcher = dispatcher;
    }

    public abstract string ProviderName { get; }

    private ResiliencePipeline ResiliencePipeline => _lazyResiliencePipeline.Value;

    public async Task<StorageResult<UploadResult>> UploadAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        using var activity = StorageTelemetry.StartActivity("upload", ProviderName, request.Path);
        try
        {
            var context = new StoragePipelineContext(request);
            await _pipeline.ExecuteAsync(context, cancellationToken);

            if (context.IsCancelled)
            {
                activity?.SetStatus(ActivityStatusCode.Error, context.CancellationReason);
                var failResult = StorageResult<UploadResult>.Failure(
                    context.CancellationReason ?? "Pipeline cancelled.", StorageErrorCode.ValidationFailed);
                if (_eventDispatcher is not null)
                    await _eventDispatcher.DispatchUploadFailedAsync(new StorageEventContext
                    {
                        ProviderName = ProviderName,
                        OperationType = "Upload",
                        Path = request.Path,
                        IsSuccess = false,
                        ErrorMessage = failResult.ErrorMessage,
                        ErrorCode = failResult.ErrorCode,
                        Duration = sw.Elapsed
                    }, cancellationToken);
                return failResult;
            }

            var result = await ExecuteWithResilienceAsync(
                () => UploadCoreAsync(context.Request, progress, cancellationToken),
                cancellationToken);

            sw.Stop();
            var bytes = request.ContentLength ?? 0;
            if (result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                StorageTelemetry.RecordUploadSuccess(ProviderName, bytes, sw.Elapsed.TotalMilliseconds, request.ContentType);
                if (_eventDispatcher is not null)
                    await _eventDispatcher.DispatchUploadCompletedAsync(new StorageEventContext
                    {
                        ProviderName = ProviderName,
                        OperationType = "Upload",
                        Path = request.Path,
                        IsSuccess = true,
                        Duration = sw.Elapsed,
                        FileSizeBytes = bytes > 0 ? bytes : null
                    }, cancellationToken);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
                StorageTelemetry.RecordError(ProviderName, "upload");
                if (_eventDispatcher is not null)
                    await _eventDispatcher.DispatchUploadFailedAsync(new StorageEventContext
                    {
                        ProviderName = ProviderName,
                        OperationType = "Upload",
                        Path = request.Path,
                        IsSuccess = false,
                        ErrorMessage = result.ErrorMessage,
                        ErrorCode = result.ErrorCode,
                        Duration = sw.Elapsed
                    }, cancellationToken);
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "upload");
            Logger.LogError(ex, "[{Provider}] Upload failed for path {Path}", ProviderName, request.Path);
            var failResult = StorageResult<UploadResult>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
            if (_eventDispatcher is not null)
                await _eventDispatcher.DispatchUploadFailedAsync(new StorageEventContext
                {
                    ProviderName = ProviderName,
                    OperationType = "Upload",
                    Path = request.Path,
                    IsSuccess = false,
                    ErrorMessage = ex.Message,
                    ErrorCode = StorageErrorCode.ProviderError,
                    Duration = sw.Elapsed
                }, cancellationToken);
            return failResult;
        }
    }

    public async Task<StorageResult<Stream>> DownloadAsync(
        DownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        using var activity = StorageTelemetry.StartActivity("download", ProviderName, request.Path);
        try
        {
            var result = await ExecuteWithResilienceAsync(
                () => DownloadCoreAsync(request, cancellationToken),
                cancellationToken);

            if (result.IsSuccess && result.Value is not null)
            {
                var processedStream = await _downloadTransforms.ApplyAsync(result.Value, request, cancellationToken);
                result = StorageResult<Stream>.Success(processedStream);
            }

            sw.Stop();
            if (result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                var downloadedBytes = result.Value is MemoryStream ms ? ms.Length : 0;
                StorageTelemetry.RecordDownloadSuccess(ProviderName, downloadedBytes, sw.Elapsed.TotalMilliseconds);
                if (_eventDispatcher is not null)
                    await _eventDispatcher.DispatchDownloadCompletedAsync(new StorageEventContext
                    {
                        ProviderName = ProviderName,
                        OperationType = "Download",
                        Path = request.Path,
                        IsSuccess = true,
                        Duration = sw.Elapsed
                    }, cancellationToken);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
                StorageTelemetry.RecordError(ProviderName, "download");
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "download");
            Logger.LogError(ex, "[{Provider}] Download failed for path {Path}", ProviderName, request.Path);
            return StorageResult<Stream>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }


    public async Task<StorageResult> DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        using var activity = StorageTelemetry.StartActivity("delete", ProviderName, path);
        try
        {
            var result = await ExecuteWithResilienceAsync(
                () => DeleteCoreAsync(path, cancellationToken),
                cancellationToken);

            sw.Stop();
            if (result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                StorageTelemetry.RecordDeleteSuccess(ProviderName, sw.Elapsed.TotalMilliseconds);
                if (_eventDispatcher is not null)
                    await _eventDispatcher.DispatchDeleteCompletedAsync(new StorageEventContext
                    {
                        ProviderName = ProviderName,
                        OperationType = "Delete",
                        Path = path,
                        IsSuccess = true,
                        Duration = sw.Elapsed
                    }, cancellationToken);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
                StorageTelemetry.RecordError(ProviderName, "delete");
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "delete");
            Logger.LogError(ex, "[{Provider}] Delete failed for path {Path}", ProviderName, path);
            return StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    public async Task<StorageResult<bool>> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExecuteWithResilienceAsync(
                () => ExistsCoreAsync(path, cancellationToken),
                cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Provider}] Exists check failed for path {Path}", ProviderName, path);
            return StorageResult<bool>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    public async Task<StorageResult<string>> GetUrlAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetUrlCoreAsync(path, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Provider}] GetUrl failed for path {Path}", ProviderName, path);
            return StorageResult<string>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    public async Task<StorageResult> CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        using var activity = StorageTelemetry.StartActivity("copy", ProviderName, sourcePath);
        try
        {
            var result = await ExecuteWithResilienceAsync(
                () => CopyCoreAsync(sourcePath, destinationPath, cancellationToken),
                cancellationToken);

            sw.Stop();
            if (result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                StorageTelemetry.RecordCopySuccess(ProviderName, sw.Elapsed.TotalMilliseconds);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
                StorageTelemetry.RecordError(ProviderName, "copy");
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "copy");
            Logger.LogError(ex, "[{Provider}] Copy failed from {Source} to {Destination}", ProviderName, sourcePath, destinationPath);
            return StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    public async Task<StorageResult> MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var copyResult = await CopyAsync(sourcePath, destinationPath, cancellationToken);
            if (!copyResult.IsSuccess) return copyResult;
            return await DeleteAsync(sourcePath, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Provider}] Move failed from {Source} to {Destination}", ProviderName, sourcePath, destinationPath);
            return StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    public async Task<StorageResult<FileMetadata>> GetMetadataAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExecuteWithResilienceAsync(
                () => GetMetadataCoreAsync(path, cancellationToken),
                cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Provider}] GetMetadata failed for path {Path}", ProviderName, path);
            return StorageResult<FileMetadata>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    public async Task<StorageResult> SetMetadataAsync(string path, IDictionary<string, string> metadata, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExecuteWithResilienceAsync(
                () => SetMetadataCoreAsync(path, metadata, cancellationToken),
                cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Provider}] SetMetadata failed for path {Path}", ProviderName, path);
            return StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    public async Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesAsync(string? prefix = null, ListOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExecuteWithResilienceAsync(
                () => ListFilesCoreAsync(prefix, options, cancellationToken),
                cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Provider}] ListFiles failed for prefix {Prefix}", ProviderName, prefix);
            return StorageResult<IReadOnlyList<FileEntry>>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    public virtual async Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(
        IEnumerable<StoragePath> paths,
        CancellationToken cancellationToken = default)
    {
        var pathList = new List<StoragePath>(paths);
        var errors = new List<BatchDeleteError>();
        var deleted = 0;

        foreach (var path in pathList)
        {
            var result = await DeleteAsync(path, cancellationToken);
            if (result.IsSuccess)
                deleted++;
            else
                errors.Add(new BatchDeleteError { Path = path, Reason = result.ErrorMessage ?? "Unknown error" });
        }

        return StorageResult<BatchDeleteResult>.Success(new BatchDeleteResult
        {
            TotalRequested = pathList.Count,
            Deleted = deleted,
            Failed = errors.Count,
            Errors = errors.AsReadOnly()
        });
    }

    public virtual async IAsyncEnumerable<FileEntry> ListAllAsync(
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? continuationToken = null;

        do
        {
            var options = new ListOptions
            {
                MaxResults = 1000,
                ContinuationToken = continuationToken
            };

            var result = await ListFilesAsync(prefix, options, cancellationToken);
            if (!result.IsSuccess || result.Value is null)
                yield break;

            foreach (var entry in result.Value)
                yield return entry;

            // If fewer results than requested were returned, we've reached the end
            continuationToken = result.Value.Count < 1000 ? null : GetNextContinuationToken(result.Value);

            if (result.Value.Count < 1000)
                break;
        }
        while (continuationToken != null);
    }

    public virtual async Task<StorageResult> DeleteFolderAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var listResult = new List<FileEntry>();
            await foreach (var entry in ListAllAsync(prefix, cancellationToken).WithCancellation(cancellationToken))
                listResult.Add(entry);
            var paths = listResult.Select(e => StoragePath.From(e.Path)).ToList();

            if (paths.Count == 0)
                return StorageResult.Success();

            var deleteResult = await DeleteManyAsync(paths, cancellationToken);
            if (!deleteResult.IsSuccess)
                return StorageResult.Failure(deleteResult.ErrorMessage ?? "Batch delete failed", deleteResult.ErrorCode);

            if (deleteResult.Value!.Failed > 0)
                return StorageResult.Failure($"Failed to delete {deleteResult.Value.Failed} of {deleteResult.Value.TotalRequested} files.");

            return StorageResult.Success();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Provider}] DeleteFolder failed for prefix {Prefix}", ProviderName, prefix);
            return StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    public virtual async Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var listResult = await ListFilesAsync(prefix, null, cancellationToken);
            if (!listResult.IsSuccess)
                return StorageResult<IReadOnlyList<string>>.Failure(listResult.ErrorMessage!, listResult.ErrorCode);

            var folders = new HashSet<string>(StringComparer.Ordinal);
            var prefixLength = prefix?.Length ?? 0;

            foreach (var entry in listResult.Value ?? Array.Empty<FileEntry>())
            {
                var relativePath = prefixLength > 0 && entry.Path.Length > prefixLength
                    ? entry.Path.Substring(prefixLength)
                    : entry.Path;

                var slashIndex = relativePath.IndexOf('/');
                if (slashIndex > 0)
                    folders.Add(relativePath.Substring(0, slashIndex));
            }

            var result = new List<string>(folders);
            result.Sort(StringComparer.Ordinal);
            return StorageResult<IReadOnlyList<string>>.Success(result.AsReadOnly());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Provider}] ListFolders failed for prefix {Prefix}", ProviderName, prefix);
            return StorageResult<IReadOnlyList<string>>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    public virtual async Task<StorageResult<UploadResult>> UploadFromUrlAsync(
        string sourceUrl,
        StoragePath destinationPath,
        string? bucketOverride = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var httpClient = _httpClientFactory?.Invoke("vali-blob") ?? new HttpClient();
            using var response = await httpClient.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType;
            var contentLength = response.Content.Headers.ContentLength;

            var stream = await response.Content.ReadAsStreamAsync();

            var request = new UploadRequest
            {
                Path = destinationPath,
                Content = stream,
                ContentType = contentType,
                ContentLength = contentLength,
                BucketOverride = bucketOverride
            };

            return await UploadAsync(request, null, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Provider}] UploadFromUrl failed for url {Url}", ProviderName, sourceUrl);
            return StorageResult<UploadResult>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    /// <summary>Returns the bucket/container to use: bucketOverride if provided, else the configured bucket.</summary>
    protected static string ResolveBucket(string? bucketOverride, string configuredBucket)
        => bucketOverride ?? configuredBucket;

    public virtual async Task<StorageResult<ResumableUploadStatus>> GetUploadStatusAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionStore = GetSessionStore();
            if (sessionStore is null)
                throw new NotImplementedException($"{ProviderName} does not support resumable upload status retrieval.");

            var uploadSession = await sessionStore.GetAsync(uploadId, cancellationToken);
            if (uploadSession is null)
                return StorageResult<ResumableUploadStatus>.Failure($"Upload session '{uploadId}' not found or expired.", StorageErrorCode.FileNotFound);

            return StorageResult<ResumableUploadStatus>.Success(new ResumableUploadStatus
            {
                UploadId = uploadId,
                Path = uploadSession.Path,
                TotalSize = uploadSession.TotalSize,
                BytesUploaded = uploadSession.BytesUploaded,
                IsComplete = uploadSession.IsComplete,
                IsAborted = uploadSession.IsAborted,
                ExpiresAt = uploadSession.ExpiresAt
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Provider}] GetUploadStatus failed for session {UploadId}", ProviderName, uploadId);
            return StorageResult<ResumableUploadStatus>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    protected virtual IResumableSessionStore? GetSessionStore() => null;

    // Abstract core methods — each provider implements these
    protected abstract Task<StorageResult<UploadResult>> UploadCoreAsync(UploadRequest request, IProgress<UploadProgress>? progress, CancellationToken cancellationToken);
    protected abstract Task<StorageResult<Stream>> DownloadCoreAsync(DownloadRequest request, CancellationToken cancellationToken);
    protected abstract Task<StorageResult> DeleteCoreAsync(string path, CancellationToken cancellationToken);
    protected abstract Task<StorageResult<bool>> ExistsCoreAsync(string path, CancellationToken cancellationToken);
    protected abstract Task<StorageResult<string>> GetUrlCoreAsync(string path, CancellationToken cancellationToken);
    protected abstract Task<StorageResult> CopyCoreAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken);
    protected abstract Task<StorageResult<FileMetadata>> GetMetadataCoreAsync(string path, CancellationToken cancellationToken);
    protected abstract Task<StorageResult> SetMetadataCoreAsync(string path, IDictionary<string, string> metadata, CancellationToken cancellationToken);
    protected abstract Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesCoreAsync(string? prefix, ListOptions? options, CancellationToken cancellationToken);

    private static string? GetNextContinuationToken(IReadOnlyList<FileEntry> entries)
    {
        // Default: use the last entry's path as a marker; providers override ListAllAsync if they support native tokens
        return entries.Count > 0 ? entries[entries.Count - 1].Path : null;
    }

    private async Task<StorageResult<T>> ExecuteWithResilienceAsync<T>(
        Func<Task<StorageResult<T>>> operation,
        CancellationToken cancellationToken)
    {
        return await ResiliencePipeline.ExecuteAsync(
            async ct => await operation(),
            cancellationToken);
    }

    private async Task<StorageResult> ExecuteWithResilienceAsync(
        Func<Task<StorageResult>> operation,
        CancellationToken cancellationToken)
    {
        return await ResiliencePipeline.ExecuteAsync(
            async ct => await operation(),
            cancellationToken);
    }

}
