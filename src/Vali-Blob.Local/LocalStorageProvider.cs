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
using ValiBlob.Local.Options;

namespace ValiBlob.Local;

/// <summary>
/// Storage provider implementation for local filesystem storage, supporting standard operations, resumable uploads, and presigned URLs.
/// </summary>
public sealed class LocalStorageProvider : BaseStorageProvider, IResumableUploadProvider, IPresignedUrlProvider
{
    private readonly LocalStorageOptions _options;
    private readonly LocalStorageResumableHandler _resumable;

    /// <summary>Gets the provider name.</summary>
    public override string ProviderName => nameof(StorageProviderType.Local);

    /// <summary>
    /// Initializes a new instance of the local storage provider.
    /// </summary>
    public LocalStorageProvider(
        ILogger<LocalStorageProvider> logger,
        IOptions<LocalStorageOptions> options,
        IOptions<ResilienceOptions> resilienceOptions,
        IOptions<EncryptionOptions> encryptionOptions,
        StoragePipelineBuilder pipeline,
        Func<string, HttpClient> httpClientFactory)
        : base(logger, resilienceOptions, encryptionOptions, pipeline, httpClientFactory)
    {
        _options = options.Value;
        _resumable = new LocalStorageResumableHandler(_options.BasePath, logger);

        if (_options.CreateIfNotExists && !string.IsNullOrEmpty(_options.BasePath))
            Directory.CreateDirectory(_options.BasePath);
    }

    // ─── Path adapters ────────────────────────────────────────────────────────

    private string ResolvePath(string storagePath)
        => LocalStoragePathHelper.ResolvePath(_options.BasePath, storagePath);

    private static string MetaPath(string resolvedPath)
        => LocalStoragePathHelper.MetaPath(resolvedPath);

    // ─── Core operations ──────────────────────────────────────────────────────

    /// <summary>
    /// Uploads a file to local storage.
    /// </summary>
    protected override async Task<StorageResult<UploadResult>> UploadCoreAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var resolvedPath = ResolvePath(request.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);

        long size;
        using (var fs = new FileStream(resolvedPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await request.Content.CopyToAsync(fs, 81920);
            await fs.FlushAsync(cancellationToken);
            size = fs.Length;
        }

        var eTag = await LocalStorageSidecarHelper.ComputeETagAsync(resolvedPath, cancellationToken);

        if (request.ContentType is not null || request.Metadata is not null)
        {
            var meta = new Dictionary<string, string>(StringComparer.Ordinal);
            if (request.ContentType is not null)
                meta["content-type"] = request.ContentType;
            if (request.Metadata is not null)
                foreach (var kvp in request.Metadata)
                    meta[kvp.Key] = kvp.Value;
            await LocalStorageSidecarHelper.WriteAsync(resolvedPath, meta, cancellationToken);
        }

        progress?.Report(new UploadProgress(size, size));

        return StorageResult<UploadResult>.Success(new UploadResult
        {
            Path = request.Path,
            SizeBytes = size,
            ETag = eTag
        });
    }

    /// <summary>
    /// Downloads a file from local storage.
    /// </summary>
    protected override Task<StorageResult<Stream>> DownloadCoreAsync(
        DownloadRequest request,
        CancellationToken cancellationToken)
    {
        var resolvedPath = ResolvePath(request.Path);

        if (!File.Exists(resolvedPath))
            return Task.FromResult(StorageResult<Stream>.Failure(
                $"File not found: {request.Path}", StorageErrorCode.FileNotFound));

        if (request.Range is not null)
        {
            var range = request.Range;
            using var fs = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(range.From, SeekOrigin.Begin);

            long bytesToRead = range.To.HasValue
                ? range.To.Value - range.From + 1
                : fs.Length - range.From;

            var buffer = new byte[bytesToRead];
            var read = 0;
            while (read < bytesToRead)
            {
                var n = fs.Read(buffer, read, (int)(bytesToRead - read));
                if (n == 0) break;
                read += n;
            }

            if (read < bytesToRead)
                Array.Resize(ref buffer, read);

            Stream rangeStream = new MemoryStream(buffer);
            return Task.FromResult(StorageResult<Stream>.Success(rangeStream));
        }

        Stream stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(StorageResult<Stream>.Success(stream));
    }

    /// <summary>
    /// Deletes a file from local storage.
    /// </summary>
    protected override Task<StorageResult> DeleteCoreAsync(string path, CancellationToken cancellationToken)
    {
        var resolvedPath = ResolvePath(path);

        if (File.Exists(resolvedPath))
            File.Delete(resolvedPath);

        var metaPath = MetaPath(resolvedPath);
        if (File.Exists(metaPath))
            File.Delete(metaPath);

        return Task.FromResult(StorageResult.Success());
    }

    /// <summary>
    /// Checks if a file exists in local storage.
    /// </summary>
    protected override Task<StorageResult<bool>> ExistsCoreAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(StorageResult<bool>.Success(File.Exists(ResolvePath(path))));

    /// <summary>
    /// Gets the public URL for a file in local storage.
    /// </summary>
    protected override Task<StorageResult<string>> GetUrlCoreAsync(string path, CancellationToken cancellationToken)
    {
        ResolvePath(path);

        if (!string.IsNullOrEmpty(_options.PublicBaseUrl))
        {
            var baseUrl = _options.PublicBaseUrl!;
            var normalizedPath = path.Replace('\\', '/').TrimStart('/');
            var url = $"{baseUrl.TrimEnd('/')}/{normalizedPath}";
            return Task.FromResult(StorageResult<string>.Success(url));
        }

        var resolvedPath = ResolvePath(path);
        var fileUri = new Uri(resolvedPath).AbsoluteUri;
        return Task.FromResult(StorageResult<string>.Success(fileUri));
    }

    /// <summary>
    /// Copies a file within local storage.
    /// </summary>
    protected override Task<StorageResult> CopyCoreAsync(
        string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        var src = ResolvePath(sourcePath);
        var dst = ResolvePath(destinationPath);

        if (!File.Exists(src))
            return Task.FromResult(StorageResult.Failure(
                $"Source not found: {sourcePath}", StorageErrorCode.FileNotFound));

        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(src, dst, overwrite: true);

        var srcMeta = MetaPath(src);
        if (File.Exists(srcMeta))
            File.Copy(srcMeta, MetaPath(dst), overwrite: true);

        return Task.FromResult(StorageResult.Success());
    }

    /// <summary>
    /// Gets metadata for a file in local storage.
    /// </summary>
    protected override async Task<StorageResult<FileMetadata>> GetMetadataCoreAsync(
        string path, CancellationToken cancellationToken)
    {
        var resolvedPath = ResolvePath(path);

        if (!File.Exists(resolvedPath))
            return StorageResult<FileMetadata>.Failure(
                $"File not found: {path}", StorageErrorCode.FileNotFound);

        var info = new FileInfo(resolvedPath);
        var sidecar = await LocalStorageSidecarHelper.ReadAsync(resolvedPath, cancellationToken);

        sidecar.TryGetValue("content-type", out var contentType);
        var custom = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kvp in sidecar)
            if (!string.Equals(kvp.Key, "content-type", StringComparison.Ordinal))
                custom[kvp.Key] = kvp.Value;

        return StorageResult<FileMetadata>.Success(new FileMetadata
        {
            Path = path,
            SizeBytes = info.Length,
            ContentType = contentType,
            LastModified = info.LastWriteTimeUtc,
            CreatedAt = info.CreationTimeUtc,
            CustomMetadata = custom
        });
    }

    /// <summary>
    /// Sets metadata for a file in local storage.
    /// </summary>
    protected override async Task<StorageResult> SetMetadataCoreAsync(
        string path, IDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        var resolvedPath = ResolvePath(path);

        if (!File.Exists(resolvedPath))
            return StorageResult.Failure($"File not found: {path}", StorageErrorCode.FileNotFound);

        var existing = await LocalStorageSidecarHelper.ReadAsync(resolvedPath, cancellationToken);
        foreach (var kvp in metadata)
            existing[kvp.Key] = kvp.Value;

        await LocalStorageSidecarHelper.WriteAsync(resolvedPath, existing, cancellationToken);
        return StorageResult.Success();
    }

    /// <summary>
    /// Lists files in local storage.
    /// </summary>
    protected override Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesCoreAsync(
        string? prefix, ListOptions? options, CancellationToken cancellationToken)
    {
        var basePath = Path.GetFullPath(_options.BasePath);

        if (!Directory.Exists(basePath))
            return Task.FromResult(StorageResult<IReadOnlyList<FileEntry>>.Success(
                Array.Empty<FileEntry>() as IReadOnlyList<FileEntry>));

        var searchDir = basePath;

        if (prefix is not null)
        {
            var prefixNormalized = prefix.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            var possibleDir = Path.GetFullPath(Path.Combine(basePath, prefixNormalized));
            if (Directory.Exists(possibleDir))
                searchDir = possibleDir;
        }

        var entries = new List<FileEntry>();
        var maxResults = options?.MaxResults ?? int.MaxValue;

        foreach (var file in Directory.EnumerateFiles(searchDir, "*", SearchOption.AllDirectories))
        {
            if (entries.Count >= maxResults)
                break;

            if (file.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var storagePath = LocalStoragePathHelper.ToStoragePath(basePath, file);

            if (prefix is not null && !storagePath.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var info = new FileInfo(file);
            entries.Add(new FileEntry
            {
                Path = storagePath,
                SizeBytes = info.Length,
                LastModified = info.LastWriteTimeUtc
            });
        }

        return Task.FromResult(StorageResult<IReadOnlyList<FileEntry>>.Success(entries.AsReadOnly()));
    }

    // ─── Folder operations ────────────────────────────────────────────────────

    /// <summary>
    /// Deletes multiple files from local storage.
    /// </summary>
    public override Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(
        IEnumerable<StoragePath> paths,
        CancellationToken cancellationToken = default)
        => LocalStorageFolderOperations.DeleteManyAsync(paths, _options.BasePath);

    /// <summary>
    /// Lists all files in local storage asynchronously.
    /// </summary>
    public override async IAsyncEnumerable<FileEntry> ListAllAsync(
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var entry in LocalStorageFolderOperations.ListAllAsync(
            _options.BasePath, prefix, cancellationToken))
        {
            yield return entry;
        }
    }

    /// <summary>
    /// Deletes a folder and all files within it from local storage.
    /// </summary>
    public override Task<StorageResult> DeleteFolderAsync(
        string prefix,
        CancellationToken cancellationToken = default)
        => LocalStorageFolderOperations.DeleteFolderAsync(_options.BasePath, prefix, Logger, ProviderName);

    /// <summary>
    /// Lists folders in local storage.
    /// </summary>
    public override Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default)
        => LocalStorageFolderOperations.ListFoldersAsync(_options.BasePath, prefix, Logger, ProviderName);

    // ─── IResumableUploadProvider ─────────────────────────────────────────────

    /// <summary>
    /// Starts a resumable upload session.
    /// </summary>
    public Task<StorageResult<ResumableUploadSession>> StartResumableUploadAsync(
        ResumableUploadRequest request,
        CancellationToken cancellationToken = default)
        => _resumable.StartAsync(request, cancellationToken);

    /// <summary>
    /// Uploads a chunk in a resumable upload session.
    /// </summary>
    public Task<StorageResult<ChunkUploadResult>> UploadChunkAsync(
        ResumableChunkRequest request,
        CancellationToken cancellationToken = default)
        => _resumable.UploadChunkAsync(request, cancellationToken);

    /// <summary>
    /// Gets the status of a resumable upload session.
    /// </summary>
    public override Task<StorageResult<ResumableUploadStatus>> GetUploadStatusAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
        => _resumable.GetStatusAsync(uploadId, cancellationToken);

    /// <summary>
    /// Completes a resumable upload session.
    /// </summary>
    public Task<StorageResult<UploadResult>> CompleteResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
        => _resumable.CompleteAsync(uploadId, cancellationToken);

    /// <summary>
    /// Aborts a resumable upload session.
    /// </summary>
    public Task<StorageResult> AbortResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
        => _resumable.AbortAsync(uploadId, cancellationToken);

    // ─── IPresignedUrlProvider ────────────────────────────────────────────────

    /// <summary>
    /// Gets a presigned upload URL for a file in local storage.
    /// </summary>
    public Task<StorageResult<string>> GetPresignedUploadUrlAsync(
        string path,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        var expires = DateTimeOffset.UtcNow.Add(expiration).ToUnixTimeSeconds();
        string url;

        if (!string.IsNullOrEmpty(_options.PublicBaseUrl))
        {
            var baseUrl = _options.PublicBaseUrl!;
            url = $"{baseUrl.TrimEnd('/')}/upload/{path.TrimStart('/')}?token={Guid.NewGuid():N}&expires={expires}";
        }
        else
        {
            var resolvedPath = ResolvePath(path);
            url = $"file://{resolvedPath}?action=upload&token={Guid.NewGuid():N}&expires={expires}";
        }

        return Task.FromResult(StorageResult<string>.Success(url));
    }

    /// <summary>
    /// Gets a presigned download URL for a file in local storage.
    /// </summary>
    public Task<StorageResult<string>> GetPresignedDownloadUrlAsync(
        string path,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        var expires = DateTimeOffset.UtcNow.Add(expiration).ToUnixTimeSeconds();
        string url;

        if (!string.IsNullOrEmpty(_options.PublicBaseUrl))
        {
            var baseUrl = _options.PublicBaseUrl!;
            url = $"{baseUrl.TrimEnd('/')}/download/{path.TrimStart('/')}?token={Guid.NewGuid():N}&expires={expires}";
        }
        else
        {
            var resolvedPath = ResolvePath(path);
            url = $"file://{resolvedPath}?token={Guid.NewGuid():N}&expires={expires}";
        }

        return Task.FromResult(StorageResult<string>.Success(url));
    }
}
