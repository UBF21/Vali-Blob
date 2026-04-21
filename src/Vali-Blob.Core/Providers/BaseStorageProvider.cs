using System.Net.Http;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Events;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using Ops = ValiBlob.Core.Providers.StorageProviderCompositeOperations;

namespace ValiBlob.Core.Providers;

public abstract class BaseStorageProvider : IStorageProvider
{
    protected readonly ILogger Logger;
    private readonly StorageOperationOrchestrator _orchestrator;
    private readonly DownloadTransformPipeline _downloadTransforms;
    private readonly Func<string, HttpClient> _httpClientFactory;
    private IReadOnlyList<string> _allowedUploadHosts = [];

    protected BaseStorageProvider(
        ILogger logger,
        IOptions<ResilienceOptions> resilienceOptions,
        IOptions<EncryptionOptions> encryptionOptions,
        StoragePipelineBuilder pipeline,
        Func<string, HttpClient> httpClientFactory)
    {
        Logger = logger;
        _httpClientFactory = httpClientFactory;
        _orchestrator = new StorageOperationOrchestrator(ProviderName, logger, resilienceOptions, pipeline);
        _downloadTransforms = new DownloadTransformPipeline(encryptionOptions.Value, this);
    }

    internal void SetEventDispatcher(StorageEventDispatcher dispatcher)
    {
        _orchestrator.SetEventDispatcher(dispatcher);
    }

    public void SetAllowedUploadHosts(IReadOnlyList<string> hosts)
    {
        _allowedUploadHosts = hosts;
    }

    public abstract string ProviderName { get; }

    // ─── Public API ───────────────────────────────────────────────────────────

    public Task<StorageResult<UploadResult>> UploadAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return _orchestrator.ExecuteUploadAsync(
            request,
            req => UploadCoreAsync(req, progress, cancellationToken),
            progress,
            cancellationToken);
    }

    public Task<StorageResult<Stream>> DownloadAsync(
        DownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        return _orchestrator.ExecuteDownloadAsync(
            request,
            req => DownloadCoreAsync(req, cancellationToken),
            (stream, req, ct) => _downloadTransforms.ApplyAsync(stream, req, ct),
            cancellationToken);
    }

    public Task<StorageResult> DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        return _orchestrator.ExecuteDeleteAsync(
            path,
            p => DeleteCoreAsync(p, cancellationToken),
            cancellationToken);
    }

    public Task<StorageResult<bool>> ExistsAsync(string path, CancellationToken cancellationToken = default)
        => _orchestrator.ExecuteGuardedAsync("Exists check", path,
            () => ExistsCoreAsync(path, cancellationToken), cancellationToken);

    public Task<StorageResult<string>> GetUrlAsync(string path, CancellationToken cancellationToken = default)
        => _orchestrator.ExecuteGuardedAsync("GetUrl", path,
            () => GetUrlCoreAsync(path, cancellationToken), cancellationToken);

    public Task<StorageResult> CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
        => _orchestrator.ExecuteCopyAsync(
            sourcePath, destinationPath,
            (src, dst) => CopyCoreAsync(src, dst, cancellationToken),
            cancellationToken);

    public Task<StorageResult> MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
        => _orchestrator.ExecuteWithErrorBoundaryAsync("Move", $"{sourcePath} -> {destinationPath}", async () =>
        {
            var copyResult = await CopyAsync(sourcePath, destinationPath, cancellationToken);
            return copyResult.IsSuccess ? await DeleteAsync(sourcePath, cancellationToken) : copyResult;
        });

    public Task<StorageResult<FileMetadata>> GetMetadataAsync(string path, CancellationToken cancellationToken = default)
        => _orchestrator.ExecuteGuardedAsync("GetMetadata", path,
            () => GetMetadataCoreAsync(path, cancellationToken), cancellationToken);

    public Task<StorageResult> SetMetadataAsync(string path, IDictionary<string, string> metadata, CancellationToken cancellationToken = default)
        => _orchestrator.ExecuteGuardedAsync("SetMetadata", path,
            () => SetMetadataCoreAsync(path, metadata, cancellationToken), cancellationToken);

    public Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesAsync(string? prefix = null, ListOptions? options = null, CancellationToken cancellationToken = default)
        => _orchestrator.ExecuteGuardedAsync("ListFiles", prefix ?? "(root)",
            () => ListFilesCoreAsync(prefix, options, cancellationToken), cancellationToken);

    // ─── Virtual higher-level operations (delegate to StorageProviderCompositeOperations) ─

    public virtual Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(
        IEnumerable<StoragePath> paths,
        CancellationToken cancellationToken = default)
        => Ops.DeleteManyAsync(this, paths, cancellationToken);

    public virtual IAsyncEnumerable<FileEntry> ListAllAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default)
        => Ops.ListAllAsync(this, prefix, cancellationToken);

    public virtual Task<StorageResult> DeleteFolderAsync(
        string prefix,
        CancellationToken cancellationToken = default)
        => Ops.DeleteFolderAsync(this, Logger, ProviderName, prefix, cancellationToken);

    public virtual Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default)
        => Ops.ListFoldersAsync(this, Logger, ProviderName, prefix, cancellationToken);

    public virtual Task<StorageResult<UploadResult>> UploadFromUrlAsync(
        string sourceUrl,
        StoragePath destinationPath,
        string? bucketOverride = null,
        CancellationToken cancellationToken = default)
        => Ops.UploadFromUrlAsync(this, Logger, ProviderName, _allowedUploadHosts, _httpClientFactory,
            sourceUrl, destinationPath, bucketOverride, cancellationToken);

    public virtual Task<StorageResult<ResumableUploadStatus>> GetUploadStatusAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
        => Ops.GetUploadStatusAsync(Logger, ProviderName, GetSessionStore, uploadId, cancellationToken);

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Returns the bucket/container to use: bucketOverride if provided, else the configured bucket.</summary>
    protected static string ResolveBucket(string? bucketOverride, string configuredBucket)
        => bucketOverride ?? configuredBucket;

    protected virtual IResumableSessionStore? GetSessionStore() => null;

    // ─── Abstract core methods ────────────────────────────────────────────────

    protected abstract Task<StorageResult<UploadResult>> UploadCoreAsync(UploadRequest request, IProgress<UploadProgress>? progress, CancellationToken cancellationToken);
    protected abstract Task<StorageResult<Stream>> DownloadCoreAsync(DownloadRequest request, CancellationToken cancellationToken);
    protected abstract Task<StorageResult> DeleteCoreAsync(string path, CancellationToken cancellationToken);
    protected abstract Task<StorageResult<bool>> ExistsCoreAsync(string path, CancellationToken cancellationToken);
    protected abstract Task<StorageResult<string>> GetUrlCoreAsync(string path, CancellationToken cancellationToken);
    protected abstract Task<StorageResult> CopyCoreAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken);
    protected abstract Task<StorageResult<FileMetadata>> GetMetadataCoreAsync(string path, CancellationToken cancellationToken);
    protected abstract Task<StorageResult> SetMetadataCoreAsync(string path, IDictionary<string, string> metadata, CancellationToken cancellationToken);
    protected abstract Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesCoreAsync(string? prefix, ListOptions? options, CancellationToken cancellationToken);
}
