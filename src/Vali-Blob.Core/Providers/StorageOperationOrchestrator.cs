using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using ValiBlob.Core.Events;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Resilience;
using ValiBlob.Core.Telemetry;

namespace ValiBlob.Core.Providers;

/// <summary>
/// Handles cross-cutting concerns for storage operations: pipeline execution,
/// resilience wrapping, telemetry recording, and event dispatching.
/// BaseStorageProvider holds one instance of this per provider.
/// </summary>
internal sealed class StorageOperationOrchestrator
{
    private readonly string _providerName;
    private readonly ILogger _logger;
    private readonly StoragePipelineBuilder _pipeline;
    private readonly Lazy<ResiliencePipeline> _lazyResiliencePipeline;

    private StorageEventDispatcher? _eventDispatcher;

    internal StorageOperationOrchestrator(
        string providerName,
        ILogger logger,
        IOptions<ResilienceOptions> resilienceOptions,
        StoragePipelineBuilder pipeline)
    {
        _providerName = providerName;
        _logger = logger;
        _pipeline = pipeline;
        _lazyResiliencePipeline = new Lazy<ResiliencePipeline>(() =>
            ResiliencePipelineFactory.BuildPipeline(resilienceOptions.Value, _logger, _providerName));
    }

    internal void SetEventDispatcher(StorageEventDispatcher dispatcher)
    {
        _eventDispatcher = dispatcher;
    }

    private ResiliencePipeline ResiliencePipeline => _lazyResiliencePipeline.Value;

    // ─── Upload ───────────────────────────────────────────────────────────────

    internal async Task<StorageResult<UploadResult>> ExecuteUploadAsync(
        UploadRequest request,
        Func<UploadRequest, Task<StorageResult<UploadResult>>> core,
        IProgress<UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var activity = StorageTelemetry.StartActivity("upload", _providerName, request.Path);
        try
        {
            var context = new StoragePipelineContext(request);
            await _pipeline.ExecuteAsync(context, cancellationToken);

            if (context.IsCancelled)
            {
                activity?.SetStatus(ActivityStatusCode.Error, context.CancellationReason);
                var failResult = StorageResult<UploadResult>.Failure(
                    context.CancellationReason ?? "Pipeline cancelled.", StorageErrorCode.ValidationFailed);
                await DispatchUploadFailedAsync(request.Path, failResult.ErrorMessage, failResult.ErrorCode, sw.Elapsed, cancellationToken);
                return failResult;
            }

            var result = await ExecuteWithResilienceAsync(
                () => core(context.Request),
                cancellationToken);

            sw.Stop();
            var bytes = request.ContentLength ?? 0;
            if (result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                StorageTelemetry.RecordUploadSuccess(_providerName, bytes, sw.Elapsed.TotalMilliseconds, request.ContentType);
                await DispatchUploadCompletedAsync(request.Path, sw.Elapsed, bytes > 0 ? bytes : null, cancellationToken);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
                StorageTelemetry.RecordError(_providerName, "upload");
                await DispatchUploadFailedAsync(request.Path, result.ErrorMessage, result.ErrorCode, sw.Elapsed, cancellationToken);
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(_providerName, "upload");
            _logger.LogError(ex, "[{Provider}] Upload failed for path {Path}", _providerName, request.Path);
            var failResult = StorageResult<UploadResult>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
            await DispatchUploadFailedAsync(request.Path, ex.Message, StorageErrorCode.ProviderError, sw.Elapsed, cancellationToken);
            return failResult;
        }
    }

    // ─── Download ─────────────────────────────────────────────────────────────

    internal async Task<StorageResult<Stream>> ExecuteDownloadAsync(
        DownloadRequest request,
        Func<DownloadRequest, Task<StorageResult<Stream>>> core,
        Func<Stream, DownloadRequest, CancellationToken, Task<Stream>> applyTransforms,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var activity = StorageTelemetry.StartActivity("download", _providerName, request.Path);
        try
        {
            var result = await ExecuteWithResilienceAsync(
                () => core(request),
                cancellationToken);

            if (result.IsSuccess && result.Value is not null)
            {
                var processedStream = await applyTransforms(result.Value, request, cancellationToken);
                result = StorageResult<Stream>.Success(processedStream);
            }

            sw.Stop();
            if (result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                var downloadedBytes = result.Value is MemoryStream ms ? ms.Length : 0;
                StorageTelemetry.RecordDownloadSuccess(_providerName, downloadedBytes, sw.Elapsed.TotalMilliseconds);
                if (_eventDispatcher is not null)
                    await _eventDispatcher.DispatchDownloadCompletedAsync(new StorageEventContext
                    {
                        ProviderName = _providerName,
                        OperationType = "Download",
                        Path = request.Path,
                        IsSuccess = true,
                        Duration = sw.Elapsed
                    }, cancellationToken);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
                StorageTelemetry.RecordError(_providerName, "download");
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(_providerName, "download");
            _logger.LogError(ex, "[{Provider}] Download failed for path {Path}", _providerName, request.Path);
            return StorageResult<Stream>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    // ─── Delete ───────────────────────────────────────────────────────────────

    internal async Task<StorageResult> ExecuteDeleteAsync(
        string path,
        Func<string, Task<StorageResult>> core,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var activity = StorageTelemetry.StartActivity("delete", _providerName, path);
        try
        {
            var result = await ExecuteWithResilienceAsync(
                () => core(path),
                cancellationToken);

            sw.Stop();
            if (result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                StorageTelemetry.RecordDeleteSuccess(_providerName, sw.Elapsed.TotalMilliseconds);
                if (_eventDispatcher is not null)
                    await _eventDispatcher.DispatchDeleteCompletedAsync(new StorageEventContext
                    {
                        ProviderName = _providerName,
                        OperationType = "Delete",
                        Path = path,
                        IsSuccess = true,
                        Duration = sw.Elapsed
                    }, cancellationToken);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
                StorageTelemetry.RecordError(_providerName, "delete");
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(_providerName, "delete");
            _logger.LogError(ex, "[{Provider}] Delete failed for path {Path}", _providerName, path);
            return StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    // ─── Copy ─────────────────────────────────────────────────────────────────

    internal async Task<StorageResult> ExecuteCopyAsync(
        string sourcePath,
        string destinationPath,
        Func<string, string, Task<StorageResult>> core,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var activity = StorageTelemetry.StartActivity("copy", _providerName, sourcePath);
        try
        {
            var result = await ExecuteWithResilienceAsync(
                () => core(sourcePath, destinationPath),
                cancellationToken);

            sw.Stop();
            if (result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                StorageTelemetry.RecordCopySuccess(_providerName, sw.Elapsed.TotalMilliseconds);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
                StorageTelemetry.RecordError(_providerName, "copy");
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(_providerName, "copy");
            _logger.LogError(ex, "[{Provider}] Copy failed from {Source} to {Destination}", _providerName, sourcePath, destinationPath);
            return StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    // ─── Generic resilience wrapper ───────────────────────────────────────────

    internal async Task<StorageResult<T>> ExecuteWithResilienceAsync<T>(
        Func<Task<StorageResult<T>>> operation,
        CancellationToken cancellationToken)
    {
        return await ResiliencePipeline.ExecuteAsync(
            async _ => await operation(),
            cancellationToken);
    }

    internal async Task<StorageResult> ExecuteWithResilienceAsync(
        Func<Task<StorageResult>> operation,
        CancellationToken cancellationToken)
    {
        return await ResiliencePipeline.ExecuteAsync(
            async _ => await operation(),
            cancellationToken);
    }

    // ─── Simple guarded wrappers (resilience + catch + log) ──────────────────

    /// <summary>Runs <paramref name="operation"/> through the resilience pipeline and converts any escaped exception to a failure result.</summary>
    internal async Task<StorageResult<T>> ExecuteGuardedAsync<T>(
        string operationName,
        string context,
        Func<Task<StorageResult<T>>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteWithResilienceAsync(operation, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Provider}] {Operation} failed for {Context}", _providerName, operationName, context);
            return StorageResult<T>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    internal async Task<StorageResult> ExecuteGuardedAsync(
        string operationName,
        string context,
        Func<Task<StorageResult>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteWithResilienceAsync(operation, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Provider}] {Operation} failed for {Context}", _providerName, operationName, context);
            return StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    /// <summary>Wraps <paramref name="operation"/> in try/catch only — no resilience retry. Use when the operation internally calls already-resilient methods.</summary>
    internal async Task<StorageResult> ExecuteWithErrorBoundaryAsync(
        string operationName,
        string context,
        Func<Task<StorageResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Provider}] {Operation} failed for {Context}", _providerName, operationName, context);
            return StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    // ─── Event dispatch helpers ───────────────────────────────────────────────

    private Task DispatchUploadCompletedAsync(string path, TimeSpan duration, long? bytes, CancellationToken cancellationToken)
    {
        if (_eventDispatcher is null) return Task.CompletedTask;
        return _eventDispatcher.DispatchUploadCompletedAsync(new StorageEventContext
        {
            ProviderName = _providerName,
            OperationType = "Upload",
            Path = path,
            IsSuccess = true,
            Duration = duration,
            FileSizeBytes = bytes
        }, cancellationToken);
    }

    private Task DispatchUploadFailedAsync(string path, string? errorMessage, StorageErrorCode? errorCode, TimeSpan duration, CancellationToken cancellationToken)
    {
        if (_eventDispatcher is null) return Task.CompletedTask;
        return _eventDispatcher.DispatchUploadFailedAsync(new StorageEventContext
        {
            ProviderName = _providerName,
            OperationType = "Upload",
            Path = path,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode ?? StorageErrorCode.ProviderError,
            Duration = duration
        }, cancellationToken);
    }
}
