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

    public async Task<StorageResult<Stream>> DownloadAsync(
        DownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        using var activity = StorageTelemetry.ActivitySource.StartActivity("download", ActivityKind.Internal);
        activity?.SetTag("storage.provider", ProviderName);
        activity?.SetTag("storage.path", request.Path);

        try
        {
            var result = await _inner.DownloadAsync(request, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                StorageTelemetry.DownloadCounter.Add(1, new[] { new KeyValuePair<string, object?>("provider", ProviderName) });
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
                StorageTelemetry.ErrorCounter.Add(1, new[]
                {
                    new KeyValuePair<string, object?>("provider", ProviderName),
                    new KeyValuePair<string, object?>("operation", "download")
                });
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
            StorageTelemetry.ErrorCounter.Add(1, new[]
                {
                    new KeyValuePair<string, object?>("provider", ProviderName),
                    new KeyValuePair<string, object?>("operation", "download")
                });
            throw;
        }
        finally
        {
            sw.Stop();
            activity?.SetTag("duration_ms", sw.ElapsedMilliseconds);
        }
    }

    public async Task<StorageResult<UploadResult>> UploadAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        using var activity = StorageTelemetry.ActivitySource.StartActivity("upload", ActivityKind.Internal);
        activity?.SetTag("storage.provider", ProviderName);
        activity?.SetTag("storage.path", request.Path);

        try
        {
            var result = await _inner.UploadAsync(request, progress, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                StorageTelemetry.UploadCounter.Add(1, new[] { new KeyValuePair<string, object?>("provider", ProviderName) });
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
                StorageTelemetry.ErrorCounter.Add(1, new[]
                {
                    new KeyValuePair<string, object?>("provider", ProviderName),
                    new KeyValuePair<string, object?>("operation", "upload")
                });
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
            StorageTelemetry.ErrorCounter.Add(1, new("provider", ProviderName), new("operation", "upload"));
            throw;
        }
        finally
        {
            sw.Stop();
            activity?.SetTag("duration_ms", sw.ElapsedMilliseconds);
        }
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
        var sw = Stopwatch.StartNew();
        using var activity = StorageTelemetry.ActivitySource.StartActivity("delete", ActivityKind.Internal);
        activity?.SetTag("storage.provider", ProviderName);
        activity?.SetTag("storage.path", path);

        try
        {
            var result = await _inner.DeleteAsync(path, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                StorageTelemetry.DeleteCounter.Add(1, new[] { new KeyValuePair<string, object?>("provider", ProviderName) });
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
                StorageTelemetry.ErrorCounter.Add(1, new[]
                {
                    new KeyValuePair<string, object?>("provider", ProviderName),
                    new KeyValuePair<string, object?>("operation", "delete")
                });
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
            StorageTelemetry.ErrorCounter.Add(1, new[]
                {
                    new KeyValuePair<string, object?>("provider", ProviderName),
                    new KeyValuePair<string, object?>("operation", "delete")
                });
            throw;
        }
        finally
        {
            sw.Stop();
            activity?.SetTag("duration_ms", sw.ElapsedMilliseconds);
        }
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
        var sw = Stopwatch.StartNew();
        using var activity = StorageTelemetry.ActivitySource.StartActivity("copy", ActivityKind.Internal);
        activity?.SetTag("storage.provider", ProviderName);
        activity?.SetTag("storage.source_path", sourcePath);
        activity?.SetTag("storage.dest_path", destinationPath);

        try
        {
            var result = await _inner.CopyAsync(sourcePath, destinationPath, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                StorageTelemetry.CopyCounter.Add(1, new[] { new KeyValuePair<string, object?>("provider", ProviderName) });
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
                StorageTelemetry.ErrorCounter.Add(1, new[]
                {
                    new KeyValuePair<string, object?>("provider", ProviderName),
                    new KeyValuePair<string, object?>("operation", "copy")
                });
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
            StorageTelemetry.ErrorCounter.Add(1, new[]
                {
                    new KeyValuePair<string, object?>("provider", ProviderName),
                    new KeyValuePair<string, object?>("operation", "copy")
                });
            throw;
        }
        finally
        {
            sw.Stop();
            activity?.SetTag("duration_ms", sw.ElapsedMilliseconds);
        }
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
