using System.Collections.Concurrent;
using System.Net.Http;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Providers;
using ValiBlob.Core.Resumable;

namespace ValiBlob.Testing;

public sealed class InMemoryStorageProvider : BaseStorageProvider, IResumableUploadProvider, IPresignedUrlProvider
{
    private readonly ConcurrentDictionary<string, StoredFile> _store = new(StringComparer.Ordinal);
    private readonly InMemoryResumableHandler _resumable;

    public override string ProviderName => nameof(StorageProviderType.InMemory);

    public InMemoryStorageProvider(
        ILogger<InMemoryStorageProvider> logger,
        IOptions<ResilienceOptions> resilienceOptions,
        IOptions<EncryptionOptions> encryptionOptions,
        StoragePipelineBuilder pipeline,
        IOptions<ResumableUploadOptions> resumableOptions,
        Func<string, HttpClient> httpClientFactory)
        : base(logger, resilienceOptions, encryptionOptions, pipeline, httpClientFactory)
    {
        _resumable = new InMemoryResumableHandler(resumableOptions.Value, ProviderName);
    }

    // ─── Store inspection helpers (for tests) ───────────────────────────────

    public bool HasFile(string path) => _store.ContainsKey(path);
    public int FileCount => _store.Count;
    public IReadOnlyCollection<string> AllPaths => _store.Keys.ToList().AsReadOnly();
    public IReadOnlyCollection<string> ActiveResumableUploadIds => _resumable.ActiveUploadIds;

    public byte[] GetRawBytes(string path)
    {
        if (!_store.TryGetValue(path, out var file))
            throw new FileNotFoundException($"File not found in memory store: {path}");
        return file.Content;
    }

    public void Clear() => _store.Clear();

    // ─── Core overrides ──────────────────────────────────────────────────────

    protected override async Task<StorageResult<UploadResult>> UploadCoreAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await request.Content.CopyToAsync(ms, 81920);
        var bytes = ms.ToArray();

        progress?.Report(new UploadProgress(bytes.Length, bytes.Length));

        return InMemoryStoreOperations.Upload(_store, request.Path, bytes, request.ContentType, request.Metadata);
    }

    protected override Task<StorageResult<Stream>> DownloadCoreAsync(
        DownloadRequest request, CancellationToken cancellationToken)
        => Task.FromResult(InMemoryStoreOperations.Download(_store, request));

    protected override Task<StorageResult> DeleteCoreAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(InMemoryStoreOperations.Delete(_store, path));

    protected override Task<StorageResult<bool>> ExistsCoreAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(InMemoryStoreOperations.Exists(_store, path));

    protected override Task<StorageResult<string>> GetUrlCoreAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(StorageResult<string>.Success($"inmemory://{path}"));

    protected override Task<StorageResult> CopyCoreAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
        => Task.FromResult(InMemoryStoreOperations.Copy(_store, sourcePath, destinationPath));

    protected override Task<StorageResult<FileMetadata>> GetMetadataCoreAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(InMemoryStoreOperations.GetMetadata(_store, path));

    protected override Task<StorageResult> SetMetadataCoreAsync(string path, IDictionary<string, string> metadata, CancellationToken cancellationToken)
        => Task.FromResult(InMemoryStoreOperations.SetMetadata(_store, path, metadata));

    protected override Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesCoreAsync(
        string? prefix, ListOptions? options, CancellationToken cancellationToken)
        => Task.FromResult(InMemoryStoreOperations.ListFiles(_store, prefix, options));

    // ─── Bulk / folder overrides ─────────────────────────────────────────────

    public override Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(
        IEnumerable<StoragePath> paths,
        CancellationToken cancellationToken = default)
        => Task.FromResult(InMemoryStoreOperations.DeleteMany(_store, paths));

    public override async IAsyncEnumerable<FileEntry> ListAllAsync(
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = _store.AsEnumerable();

        if (prefix is not null)
            query = query.Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.Ordinal));

        foreach (var kvp in query)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new FileEntry
            {
                Path = kvp.Key,
                SizeBytes = kvp.Value.Content.Length,
                ContentType = kvp.Value.ContentType
            };
        }

        await Task.CompletedTask;
    }

    public override Task<StorageResult> DeleteFolderAsync(
        string prefix,
        CancellationToken cancellationToken = default)
        => Task.FromResult(InMemoryStoreOperations.DeleteFolder(_store, prefix));

    public override Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(InMemoryStoreOperations.ListFolders(_store, prefix));

    // ─── IResumableUploadProvider ────────────────────────────────────────────

    public Task<StorageResult<ResumableUploadSession>> StartResumableUploadAsync(
        ResumableUploadRequest request,
        CancellationToken cancellationToken = default)
        => _resumable.StartAsync(request, cancellationToken);

    public Task<StorageResult<ChunkUploadResult>> UploadChunkAsync(
        ResumableChunkRequest request,
        CancellationToken cancellationToken = default)
        => _resumable.UploadChunkAsync(request, cancellationToken);

    public override Task<StorageResult<ResumableUploadStatus>> GetUploadStatusAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
        => _resumable.GetStatusAsync(uploadId, cancellationToken);

    public Task<StorageResult<UploadResult>> CompleteResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
        => _resumable.CompleteAsync(uploadId, _store, cancellationToken);

    public Task<StorageResult> AbortResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
        => _resumable.AbortAsync(uploadId, cancellationToken);

    public override Task<StorageResult<UploadResult>> UploadFromUrlAsync(
        string sourceUrl,
        StoragePath destinationPath,
        string? bucketOverride = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(StorageResult<UploadResult>.Failure(
            "UploadFromUrl is not supported by the InMemory provider.",
            StorageErrorCode.NotSupported));

    // ─── IPresignedUrlProvider (stub — for testing only) ────────────────────

    public Task<StorageResult<string>> GetPresignedUploadUrlAsync(
        string path,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
        => Task.FromResult(StorageResult<string>.Success(
            $"inmemory-signed://upload/{path}?expires={DateTimeOffset.UtcNow.Add(expiration).ToUnixTimeSeconds()}"));

    public Task<StorageResult<string>> GetPresignedDownloadUrlAsync(
        string path,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
        => Task.FromResult(StorageResult<string>.Success(
            $"inmemory-signed://download/{path}?expires={DateTimeOffset.UtcNow.Add(expiration).ToUnixTimeSeconds()}"));
}
