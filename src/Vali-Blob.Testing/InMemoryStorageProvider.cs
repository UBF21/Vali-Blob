using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Providers;
using ValiBlob.Core.Resumable;
using ValiBlob.Core.Telemetry;

namespace ValiBlob.Testing;

public sealed class InMemoryStorageProvider : BaseStorageProvider, IResumableUploadProvider, IPresignedUrlProvider
{
    private readonly ConcurrentDictionary<string, StoredFile> _store = new(StringComparer.Ordinal);

    // Resumable upload state: uploadId → (session, chunk buffer)
    private readonly ConcurrentDictionary<string, (ResumableUploadSession Session, SortedChunkBuffer Buffer)>
        _resumableSessions = new(StringComparer.Ordinal);

    public override string ProviderName => "InMemory";

    public InMemoryStorageProvider(
        ILogger<InMemoryStorageProvider> logger,
        IOptions<ResilienceOptions> resilienceOptions,
        IOptions<EncryptionOptions> encryptionOptions,
        StoragePipelineBuilder pipeline)
        : base(logger, resilienceOptions, encryptionOptions, pipeline) { }

    public bool HasFile(string path) => _store.ContainsKey(path);
    public int FileCount => _store.Count;
    public IReadOnlyCollection<string> AllPaths => _store.Keys.ToList().AsReadOnly();

    public byte[] GetRawBytes(string path)
    {
        if (!_store.TryGetValue(path, out var file))
            throw new FileNotFoundException($"File not found in memory store: {path}");
        return file.Content;
    }

    public void Clear() => _store.Clear();

    protected override async Task<StorageResult<UploadResult>> UploadCoreAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        // CopyToAsync(stream, CancellationToken) is only available in .NET 6+.
        // Use the bufferSize overload for netstandard2.0/2.1 compatibility.
        await request.Content.CopyToAsync(ms, 81920);
        var bytes = ms.ToArray();

        _store[request.Path] = new StoredFile(bytes, request.ContentType, request.Metadata);

        progress?.Report(new UploadProgress(bytes.Length, bytes.Length));

        return StorageResult<UploadResult>.Success(new UploadResult
        {
            Path = request.Path,
            SizeBytes = bytes.Length,
            ETag = ComputeETag(bytes)
        });
    }

    protected override Task<StorageResult<Stream>> DownloadCoreAsync(
        DownloadRequest request, CancellationToken cancellationToken)
    {
        if (!_store.TryGetValue(request.Path, out var file))
            return Task.FromResult(StorageResult<Stream>.Failure(
                $"File not found: {request.Path}", StorageErrorCode.FileNotFound));

        Stream stream = new MemoryStream(file.Content);
        return Task.FromResult(StorageResult<Stream>.Success(stream));
    }

    protected override Task<StorageResult> DeleteCoreAsync(string path, CancellationToken cancellationToken)
    {
        _store.TryRemove(path, out _);
        return Task.FromResult(StorageResult.Success());
    }

    protected override Task<StorageResult<bool>> ExistsCoreAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(StorageResult<bool>.Success(_store.ContainsKey(path)));

    protected override Task<StorageResult<string>> GetUrlCoreAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(StorageResult<string>.Success($"inmemory://{path}"));

    protected override Task<StorageResult> CopyCoreAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        if (!_store.TryGetValue(sourcePath, out var file))
            return Task.FromResult(StorageResult.Failure($"Source not found: {sourcePath}", StorageErrorCode.FileNotFound));

        _store[destinationPath] = file;
        return Task.FromResult(StorageResult.Success());
    }

    protected override Task<StorageResult<FileMetadata>> GetMetadataCoreAsync(string path, CancellationToken cancellationToken)
    {
        if (!_store.TryGetValue(path, out var file))
            return Task.FromResult(StorageResult<FileMetadata>.Failure(
                $"File not found: {path}", StorageErrorCode.FileNotFound));

        return Task.FromResult(StorageResult<FileMetadata>.Success(new FileMetadata
        {
            Path = path,
            SizeBytes = file.Content.Length,
            ContentType = file.ContentType,
            CustomMetadata = file.Metadata ?? new Dictionary<string, string>()
        }));
    }

    protected override Task<StorageResult> SetMetadataCoreAsync(string path, IDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        if (!_store.TryGetValue(path, out var file))
            return Task.FromResult(StorageResult.Failure($"File not found: {path}", StorageErrorCode.FileNotFound));

        _store[path] = file with { Metadata = metadata };
        return Task.FromResult(StorageResult.Success());
    }

    protected override Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesCoreAsync(
        string? prefix, ListOptions? options, CancellationToken cancellationToken)
    {
        var query = _store.AsEnumerable();

        if (prefix is not null)
            query = query.Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.Ordinal));

        var entries = query
            .Take(options?.MaxResults ?? int.MaxValue)
            .Select(kvp => new FileEntry
            {
                Path = kvp.Key,
                SizeBytes = kvp.Value.Content.Length,
                ContentType = kvp.Value.ContentType
            })
            .ToList();

        return Task.FromResult(StorageResult<IReadOnlyList<FileEntry>>.Success(entries.AsReadOnly()));
    }

    public override Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(
        IEnumerable<StoragePath> paths,
        CancellationToken cancellationToken = default)
    {
        var pathList = new List<StoragePath>(paths);
        var deleted = 0;

        foreach (var path in pathList)
        {
            if (_store.TryRemove(path, out _))
                deleted++;
        }

        return Task.FromResult(StorageResult<BatchDeleteResult>.Success(new BatchDeleteResult
        {
            TotalRequested = pathList.Count,
            Deleted = deleted,
            Failed = pathList.Count - deleted,
            Errors = Array.Empty<BatchDeleteError>()
        }));
    }

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
    {
        var keysToRemove = _store.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        foreach (var key in keysToRemove)
            _store.TryRemove(key, out _);

        return Task.FromResult(StorageResult.Success());
    }

    public override Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        var query = _store.Keys.AsEnumerable();

        if (prefix is not null)
            query = query.Where(k => k.StartsWith(prefix, StringComparison.Ordinal));

        var prefixLength = prefix?.Length ?? 0;
        var folders = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in query)
        {
            var relativePath = prefixLength > 0 && key.Length > prefixLength
                ? key.Substring(prefixLength)
                : key;

            var slashIndex = relativePath.IndexOf('/');
            if (slashIndex > 0)
                folders.Add(relativePath.Substring(0, slashIndex));
        }

        var result = new List<string>(folders);
        result.Sort(StringComparer.Ordinal);
        return Task.FromResult(StorageResult<IReadOnlyList<string>>.Success(result.AsReadOnly()));
    }

    // ─── IResumableUploadProvider (in-memory, for testing) ──────────────────

    public Task<StorageResult<ResumableUploadSession>> StartResumableUploadAsync(
        ResumableUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.start", ProviderName, request.Path);
        var uploadId = Guid.NewGuid().ToString("N");
        var session = new ResumableUploadSession
        {
            UploadId = uploadId,
            Path = request.Path,
            BucketOverride = request.BucketOverride,
            TotalSize = request.TotalSize,
            BytesUploaded = 0,
            ContentType = request.ContentType,
            Metadata = request.Metadata,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
        };

        _resumableSessions[uploadId] = (session, new SortedChunkBuffer());
        StorageTelemetry.RecordResumableStarted(ProviderName);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return Task.FromResult(StorageResult<ResumableUploadSession>.Success(session));
    }

    public async Task<StorageResult<ChunkUploadResult>> UploadChunkAsync(
        ResumableChunkRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.chunk", ProviderName, request.UploadId);
        if (!_resumableSessions.TryGetValue(request.UploadId, out var entry))
        {
            StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
            activity?.SetStatus(ActivityStatusCode.Error, "Session not found");
            return StorageResult<ChunkUploadResult>.Failure($"Session '{request.UploadId}' not found.", StorageErrorCode.FileNotFound);
        }

        if (entry.Session.IsAborted)
        {
            StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
            activity?.SetStatus(ActivityStatusCode.Error, "Session aborted");
            return StorageResult<ChunkUploadResult>.Failure("Session has been aborted.", StorageErrorCode.ValidationFailed);
        }

        var chunkBytes = await StreamReadHelper.ReadChunkAsync(request.Data, request.Length, cancellationToken)
            .ConfigureAwait(false);

        // Checksum validation
        if (request.ExpectedMd5 is not null)
        {
            var actualMd5 = ChunkChecksumHelper.ComputeMd5Base64(chunkBytes);
            var error = ChunkChecksumHelper.Validate(actualMd5, request.ExpectedMd5);
            if (error is not null)
            {
                StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
                activity?.SetStatus(ActivityStatusCode.Error, error);
                return StorageResult<ChunkUploadResult>.Failure(error, StorageErrorCode.ValidationFailed);
            }
        }

        var isNewChunk = entry.Buffer.Write(request.Offset, chunkBytes);
        if (isNewChunk)
            entry.Session.BytesUploaded += chunkBytes.Length;

        StorageTelemetry.RecordResumableChunk(ProviderName, chunkBytes.Length);
        activity?.SetStatus(ActivityStatusCode.Ok);

        var isReady = entry.Session.BytesUploaded >= entry.Session.TotalSize;
        return StorageResult<ChunkUploadResult>.Success(new ChunkUploadResult
        {
            UploadId = request.UploadId,
            BytesUploaded = entry.Session.BytesUploaded,
            TotalSize = entry.Session.TotalSize,
            IsReadyToComplete = isReady
        });
    }

    public override Task<StorageResult<ResumableUploadStatus>> GetUploadStatusAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
    {
        if (!_resumableSessions.TryGetValue(uploadId, out var entry))
            return Task.FromResult(StorageResult<ResumableUploadStatus>.Failure($"Session '{uploadId}' not found.", StorageErrorCode.FileNotFound));

        return Task.FromResult(StorageResult<ResumableUploadStatus>.Success(new ResumableUploadStatus
        {
            UploadId = uploadId,
            Path = entry.Session.Path,
            TotalSize = entry.Session.TotalSize,
            BytesUploaded = entry.Session.BytesUploaded,
            IsComplete = entry.Session.IsComplete,
            IsAborted = entry.Session.IsAborted,
            ExpiresAt = entry.Session.ExpiresAt
        }));
    }

    public Task<StorageResult<UploadResult>> CompleteResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.complete", ProviderName, uploadId);
        if (!_resumableSessions.TryGetValue(uploadId, out var entry))
        {
            StorageTelemetry.RecordError(ProviderName, "resumable.complete");
            activity?.SetStatus(ActivityStatusCode.Error, "Session not found");
            return Task.FromResult(StorageResult<UploadResult>.Failure($"Session '{uploadId}' not found.", StorageErrorCode.FileNotFound));
        }

        if (entry.Session.IsAborted)
        {
            StorageTelemetry.RecordError(ProviderName, "resumable.complete");
            activity?.SetStatus(ActivityStatusCode.Error, "Session aborted");
            return Task.FromResult(StorageResult<UploadResult>.Failure("Session has been aborted.", StorageErrorCode.ValidationFailed));
        }

        var assembled = entry.Buffer.Assemble();
        _store[entry.Session.Path] = new StoredFile(assembled, entry.Session.ContentType, entry.Session.Metadata);
        entry.Session.IsComplete = true;
        _resumableSessions.TryRemove(uploadId, out _);

        StorageTelemetry.RecordResumableCompleted(ProviderName);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return Task.FromResult(StorageResult<UploadResult>.Success(new UploadResult
        {
            Path = entry.Session.Path,
            ETag = ComputeETag(assembled),
            SizeBytes = assembled.Length
        }));
    }

    public Task<StorageResult> AbortResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.abort", ProviderName, uploadId);
        if (!_resumableSessions.TryGetValue(uploadId, out var entry))
        {
            StorageTelemetry.RecordError(ProviderName, "resumable.abort");
            activity?.SetStatus(ActivityStatusCode.Error, "Session not found");
            return Task.FromResult(StorageResult.Failure(
                $"Upload session '{uploadId}' not found or expired.",
                StorageErrorCode.FileNotFound));
        }

        entry.Session.IsAborted = true;
        _resumableSessions.TryRemove(uploadId, out _);
        StorageTelemetry.RecordResumableAborted(ProviderName);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return Task.FromResult(StorageResult.Success());
    }

    /// <summary>Returns all upload IDs for active (non-complete, non-aborted) resumable sessions. Useful in tests.</summary>
    public IReadOnlyCollection<string> ActiveResumableUploadIds =>
        _resumableSessions.Keys.ToList().AsReadOnly();

    // ─── Helpers ────────────────────────────────────────────────────────────

    private sealed class SortedChunkBuffer
    {
        // offset → data (chunks may arrive out of order in tests)
        private readonly SortedDictionary<long, byte[]> _chunks = new();

        /// <summary>Writes a chunk. Returns true if this offset was not previously seen (new bytes).</summary>
        public bool Write(long offset, byte[] data)
        {
            var isNew = !_chunks.ContainsKey(offset);
            _chunks[offset] = data;
            return isNew;
        }

        public byte[] Assemble()
        {
            using var ms = new MemoryStream();
            foreach (var kvp in _chunks)
                ms.Write(kvp.Value, 0, kvp.Value.Length);
            return ms.ToArray();
        }
    }

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

    private static string ComputeETag(byte[] bytes)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        return Convert.ToBase64String(md5.ComputeHash(bytes));
    }

    private sealed record StoredFile(
        byte[] Content,
        string? ContentType,
        IDictionary<string, string>? Metadata);
}
