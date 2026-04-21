using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Telemetry;

namespace ValiBlob.Core.Providers;

/// <summary>
/// Decorator that adds OpenTelemetry instrumentation to any IStorageProvider.
/// Tracks operation timing, creates activities, and records metrics.
/// </summary>
public sealed class StorageTelemetryDecorator : IStorageProvider
{
    private readonly IStorageProvider _inner;

    public StorageTelemetryDecorator(IStorageProvider inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public string ProviderName => _inner.ProviderName;

    public Task<StorageResult<Stream>> DownloadAsync(
        DownloadRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteWithTelemetryAsync(
            "download",
            request.Path,
            StorageTelemetry.DownloadCounter,
            ct => _inner.DownloadAsync(request, ct),
            cancellationToken);

    public Task<StorageResult<UploadResult>> UploadAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        ExecuteWithTelemetryAsync(
            "upload",
            request.Path,
            StorageTelemetry.UploadCounter,
            ct => _inner.UploadAsync(request, progress, ct),
            cancellationToken);

    public async Task<StorageResult<bool>> ExistsAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        await _inner.ExistsAsync(path, cancellationToken).ConfigureAwait(false);

    public async Task<StorageResult<string>> GetUrlAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        await _inner.GetUrlAsync(path, cancellationToken).ConfigureAwait(false);

    public Task<StorageResult> DeleteAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        ExecuteWithTelemetryAsync(
            "delete",
            path,
            StorageTelemetry.DeleteCounter,
            ct => _inner.DeleteAsync(path, ct),
            cancellationToken);

    public async Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesAsync(
        string? prefix = null,
        ListOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await _inner.ListFilesAsync(prefix, options, cancellationToken).ConfigureAwait(false);

    public async Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(
        IEnumerable<StoragePath> paths,
        CancellationToken cancellationToken = default) =>
        await _inner.DeleteManyAsync(paths, cancellationToken).ConfigureAwait(false);

    public IAsyncEnumerable<FileEntry> ListAllAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default) =>
        _inner.ListAllAsync(prefix, cancellationToken);

    public async Task<StorageResult> DeleteFolderAsync(
        string prefix,
        CancellationToken cancellationToken = default) =>
        await _inner.DeleteFolderAsync(prefix, cancellationToken).ConfigureAwait(false);

    public async Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default) =>
        await _inner.ListFoldersAsync(prefix, cancellationToken).ConfigureAwait(false);

    public async Task<StorageResult<FileMetadata>> GetMetadataAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        await _inner.GetMetadataAsync(path, cancellationToken).ConfigureAwait(false);

    public Task<StorageResult> CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        ExecuteWithTelemetryAsync(
            "copy",
            sourcePath,
            StorageTelemetry.CopyCounter,
            ct => _inner.CopyAsync(sourcePath, destinationPath, ct),
            cancellationToken,
            extraTags: new[] { new KeyValuePair<string, object?>("storage.dest_path", destinationPath) });

    public async Task<StorageResult> MoveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        await _inner.MoveAsync(sourcePath, destinationPath, cancellationToken).ConfigureAwait(false);

    public async Task<StorageResult<UploadResult>> UploadFromUrlAsync(
        string sourceUrl,
        StoragePath destinationPath,
        string? bucketOverride = null,
        CancellationToken cancellationToken = default) =>
        await _inner.UploadFromUrlAsync(sourceUrl, destinationPath, bucketOverride, cancellationToken).ConfigureAwait(false);

    // --- Helpers ---

    private async Task<StorageResult<T>> ExecuteWithTelemetryAsync<T>(
        string operationName,
        string path,
        System.Diagnostics.Metrics.Counter<long> successCounter,
        Func<CancellationToken, Task<StorageResult<T>>> operation,
        CancellationToken cancellationToken,
        KeyValuePair<string, object?>[]? extraTags = null)
    {
        var sw = Stopwatch.StartNew();
        using var activity = StorageTelemetry.ActivitySource.StartActivity(operationName, ActivityKind.Internal);
        activity?.SetTag("storage.provider", ProviderName);
        activity?.SetTag("storage.path", path);
        if (extraTags is not null)
            foreach (var tag in extraTags)
                activity?.SetTag(tag.Key, tag.Value);

        try
        {
            var result = await operation(cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                successCounter.Add(1, new[] { new KeyValuePair<string, object?>("provider", ProviderName) });
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
                RecordError(activity, operationName);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            RecordError(activity, operationName);
            throw;
        }
        finally
        {
            sw.Stop();
            activity?.SetTag("duration_ms", sw.ElapsedMilliseconds);
        }
    }

    private async Task<StorageResult> ExecuteWithTelemetryAsync(
        string operationName,
        string path,
        System.Diagnostics.Metrics.Counter<long> successCounter,
        Func<CancellationToken, Task<StorageResult>> operation,
        CancellationToken cancellationToken,
        KeyValuePair<string, object?>[]? extraTags = null)
    {
        var sw = Stopwatch.StartNew();
        using var activity = StorageTelemetry.ActivitySource.StartActivity(operationName, ActivityKind.Internal);
        activity?.SetTag("storage.provider", ProviderName);
        activity?.SetTag("storage.path", path);
        if (extraTags is not null)
            foreach (var tag in extraTags)
                activity?.SetTag(tag.Key, tag.Value);

        try
        {
            var result = await operation(cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                successCounter.Add(1, new[] { new KeyValuePair<string, object?>("provider", ProviderName) });
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
                RecordError(activity, operationName);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            RecordError(activity, operationName);
            throw;
        }
        finally
        {
            sw.Stop();
            activity?.SetTag("duration_ms", sw.ElapsedMilliseconds);
        }
    }

    private void RecordError(Activity? activity, string operationName)
    {
        StorageTelemetry.ErrorCounter.Add(1, new[]
        {
            new KeyValuePair<string, object?>("provider", ProviderName),
            new KeyValuePair<string, object?>("operation", operationName)
        });
    }
}
