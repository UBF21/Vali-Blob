using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Providers;
using ValiBlob.Core.Resumable;
using ValiBlob.Core.Telemetry;
using ValiBlob.Local.Options;

namespace ValiBlob.Local;

public sealed class LocalStorageProvider : BaseStorageProvider, IResumableUploadProvider, IPresignedUrlProvider
{
    private readonly LocalStorageOptions _options;

    public override string ProviderName => "Local";

    public LocalStorageProvider(
        ILogger<LocalStorageProvider> logger,
        IOptions<LocalStorageOptions> options,
        IOptions<ResilienceOptions> resilienceOptions,
        IOptions<EncryptionOptions> encryptionOptions,
        StoragePipelineBuilder pipeline)
        : base(logger, resilienceOptions, encryptionOptions, pipeline)
    {
        _options = options.Value;

        if (_options.CreateIfNotExists && !string.IsNullOrEmpty(_options.BasePath))
            Directory.CreateDirectory(_options.BasePath);
    }

    // ─── Path helpers ─────────────────────────────────────────────────────────

    private string ResolvePath(string storagePath)
    {
        var basePath = Path.GetFullPath(_options.BasePath);
        // Normalize the storage path — convert forward slashes to OS separator
        var normalized = storagePath.Replace('/', Path.DirectorySeparatorChar)
                                    .TrimStart(Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(basePath, normalized));

        // Validate no path traversal
        if (!fullPath.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, basePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Path traversal detected: '{storagePath}'");
        }

        return fullPath;
    }

    private string MetaPath(string resolvedPath) => resolvedPath + ".meta.json";

    private static string ToStoragePath(string basePath, string fullPath)
    {
        var relative = fullPath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    // ─── Core operations ──────────────────────────────────────────────────────

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

        // Compute ETag from file bytes (write stream is closed above)
        string eTag;
        using (var hashFs = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            using var md5 = MD5.Create();
            eTag = Convert.ToBase64String(md5.ComputeHash(hashFs));
        }

        // Save metadata sidecar if ContentType or Metadata is provided
        if (request.ContentType is not null || request.Metadata is not null)
        {
            var meta = new Dictionary<string, string>(StringComparer.Ordinal);
            if (request.ContentType is not null)
                meta["content-type"] = request.ContentType;
            if (request.Metadata is not null)
                foreach (var kvp in request.Metadata)
                    meta[kvp.Key] = kvp.Value;
            await WriteSidecarAsync(resolvedPath, meta, cancellationToken);
        }

        progress?.Report(new UploadProgress(size, size));

        return StorageResult<UploadResult>.Success(new UploadResult
        {
            Path = request.Path,
            SizeBytes = size,
            ETag = eTag
        });
    }

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
            // Range download — copy the requested bytes into a MemoryStream
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

    protected override Task<StorageResult<bool>> ExistsCoreAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(StorageResult<bool>.Success(File.Exists(ResolvePath(path))));

    protected override Task<StorageResult<string>> GetUrlCoreAsync(string path, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_options.PublicBaseUrl))
        {
            var baseUrl = _options.PublicBaseUrl!;
            var url = $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
            return Task.FromResult(StorageResult<string>.Success(url));
        }

        var resolvedPath = ResolvePath(path);
        var fileUri = new Uri(resolvedPath).AbsoluteUri;
        return Task.FromResult(StorageResult<string>.Success(fileUri));
    }

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

        // Also copy sidecar if present
        var srcMeta = MetaPath(src);
        if (File.Exists(srcMeta))
            File.Copy(srcMeta, MetaPath(dst), overwrite: true);

        return Task.FromResult(StorageResult.Success());
    }

    protected override async Task<StorageResult<FileMetadata>> GetMetadataCoreAsync(
        string path, CancellationToken cancellationToken)
    {
        var resolvedPath = ResolvePath(path);

        if (!File.Exists(resolvedPath))
            return StorageResult<FileMetadata>.Failure(
                $"File not found: {path}", StorageErrorCode.FileNotFound);

        var info = new FileInfo(resolvedPath);
        var sidecar = await ReadSidecarAsync(resolvedPath, cancellationToken);

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

    protected override async Task<StorageResult> SetMetadataCoreAsync(
        string path, IDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        var resolvedPath = ResolvePath(path);

        if (!File.Exists(resolvedPath))
            return StorageResult.Failure($"File not found: {path}", StorageErrorCode.FileNotFound);

        // Merge with existing sidecar
        var existing = await ReadSidecarAsync(resolvedPath, cancellationToken);
        foreach (var kvp in metadata)
            existing[kvp.Key] = kvp.Value;

        await WriteSidecarAsync(resolvedPath, existing, cancellationToken);
        return StorageResult.Success();
    }

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
            // If prefix has directory segments, restrict the search
            var prefixNormalized = prefix.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            var possibleDir = Path.GetFullPath(Path.Combine(basePath, prefixNormalized));
            if (Directory.Exists(possibleDir))
                searchDir = possibleDir;
        }

        var entries = new List<FileEntry>();
        var maxResults = options?.MaxResults ?? int.MaxValue;

        var allFiles = Directory.EnumerateFiles(searchDir, "*", SearchOption.AllDirectories);

        foreach (var file in allFiles)
        {
            if (entries.Count >= maxResults)
                break;

            // Skip sidecar meta files
            if (file.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var storagePath = ToStoragePath(basePath, file);

            // Filter by prefix
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

    // ─── Overridden batch/folder operations ───────────────────────────────────

    public override Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(
        IEnumerable<StoragePath> paths,
        CancellationToken cancellationToken = default)
    {
        var pathList = new List<StoragePath>(paths);
        var deleted = 0;
        var errors = new List<BatchDeleteError>();

        foreach (var p in pathList)
        {
            try
            {
                var resolvedPath = ResolvePath(p);
                if (File.Exists(resolvedPath))
                {
                    File.Delete(resolvedPath);
                    var meta = MetaPath(resolvedPath);
                    if (File.Exists(meta)) File.Delete(meta);
                }
                deleted++;
            }
            catch (Exception ex)
            {
                errors.Add(new BatchDeleteError { Path = p, Reason = ex.Message });
            }
        }

        return Task.FromResult(StorageResult<BatchDeleteResult>.Success(new BatchDeleteResult
        {
            TotalRequested = pathList.Count,
            Deleted = deleted,
            Failed = errors.Count,
            Errors = errors.AsReadOnly()
        }));
    }

    public override async IAsyncEnumerable<FileEntry> ListAllAsync(
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var basePath = Path.GetFullPath(_options.BasePath);

        if (!Directory.Exists(basePath))
            yield break;

        var allFiles = Directory.EnumerateFiles(basePath, "*", SearchOption.AllDirectories);

        foreach (var file in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var storagePath = ToStoragePath(basePath, file);

            if (prefix is not null && !storagePath.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var info = new FileInfo(file);
            yield return new FileEntry
            {
                Path = storagePath,
                SizeBytes = info.Length,
                LastModified = info.LastWriteTimeUtc
            };

            await Task.CompletedTask;
        }
    }

    public override Task<StorageResult> DeleteFolderAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var basePath = Path.GetFullPath(_options.BasePath);
            var normalized = prefix.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            var folderPath = Path.GetFullPath(Path.Combine(basePath, normalized));

            if (Directory.Exists(folderPath))
                Directory.Delete(folderPath, recursive: true);

            return Task.FromResult(StorageResult.Success());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Provider}] DeleteFolder failed for prefix {Prefix}", ProviderName, prefix);
            return Task.FromResult(StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex));
        }
    }

    public override Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var basePath = Path.GetFullPath(_options.BasePath);
            var searchDir = basePath;

            if (prefix is not null)
            {
                var normalized = prefix.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                var possibleDir = Path.GetFullPath(Path.Combine(basePath, normalized));
                if (Directory.Exists(possibleDir))
                    searchDir = possibleDir;
            }

            if (!Directory.Exists(searchDir))
                return Task.FromResult(StorageResult<IReadOnlyList<string>>.Success(
                    Array.Empty<string>() as IReadOnlyList<string>));

            var directories = Directory.GetDirectories(searchDir)
                .Select(d => Path.GetFileName(d)!)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult(StorageResult<IReadOnlyList<string>>.Success(
                directories.AsReadOnly() as IReadOnlyList<string>));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Provider}] ListFolders failed for prefix {Prefix}", ProviderName, prefix);
            return Task.FromResult(StorageResult<IReadOnlyList<string>>.Failure(
                ex.Message, StorageErrorCode.ProviderError, ex));
        }
    }

    // ─── Sidecar helpers ──────────────────────────────────────────────────────

    private async Task<Dictionary<string, string>> ReadSidecarAsync(string resolvedPath, CancellationToken cancellationToken)
    {
        var metaPath = MetaPath(resolvedPath);
        if (!File.Exists(metaPath))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            string json;
            using (var reader = new StreamReader(metaPath, Encoding.UTF8))
                json = await reader.ReadToEndAsync();
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return dict ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static async Task WriteSidecarAsync(
        string resolvedPath,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        var metaPath = resolvedPath + ".meta.json";
        var json = JsonSerializer.Serialize(metadata);
        using var writer = new StreamWriter(metaPath, append: false, Encoding.UTF8);
        await writer.WriteAsync(json);
    }

    // ─── IResumableUploadProvider ─────────────────────────────────────────────

    public Task<StorageResult<ResumableUploadSession>> StartResumableUploadAsync(
        ResumableUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.start", ProviderName, request.Path);

        var uploadId = Guid.NewGuid().ToString("N");
        var sessionDir = GetSessionDir(uploadId);
        Directory.CreateDirectory(sessionDir);

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

        SaveSessionJson(sessionDir, session);

        StorageTelemetry.RecordResumableStarted(ProviderName);
        activity?.SetStatus(ActivityStatusCode.Ok);

        return Task.FromResult(StorageResult<ResumableUploadSession>.Success(session));
    }

    public async Task<StorageResult<ChunkUploadResult>> UploadChunkAsync(
        ResumableChunkRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.chunk", ProviderName, request.UploadId);

        var sessionDir = GetSessionDir(request.UploadId);
        if (!Directory.Exists(sessionDir))
        {
            StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
            activity?.SetStatus(ActivityStatusCode.Error, "Session not found");
            return StorageResult<ChunkUploadResult>.Failure(
                $"Session '{request.UploadId}' not found.", StorageErrorCode.FileNotFound);
        }

        var session = LoadSessionJson(sessionDir);
        if (session is null)
        {
            StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
            return StorageResult<ChunkUploadResult>.Failure(
                $"Session '{request.UploadId}' not found.", StorageErrorCode.FileNotFound);
        }

        if (session.IsAborted)
        {
            StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
            activity?.SetStatus(ActivityStatusCode.Error, "Session aborted");
            return StorageResult<ChunkUploadResult>.Failure("Session has been aborted.", StorageErrorCode.ValidationFailed);
        }

        // Read chunk bytes
        byte[] chunkBytes;
        if (request.Length.HasValue)
        {
            chunkBytes = new byte[request.Length.Value];
            var read = 0;
            while (read < chunkBytes.Length)
            {
                var n = await request.Data.ReadAsync(chunkBytes, read, chunkBytes.Length - read, cancellationToken);
                if (n == 0) break;
                read += n;
            }
            if (read < chunkBytes.Length) Array.Resize(ref chunkBytes, read);
        }
        else
        {
            using var ms = new MemoryStream();
            await request.Data.CopyToAsync(ms, 81920);
            chunkBytes = ms.ToArray();
        }

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

        // Write chunk to disk
        var chunkFile = Path.Combine(sessionDir, $"{request.Offset}.chunk");
        var isNewChunk = !File.Exists(chunkFile);
        using (var chunkFs = new FileStream(chunkFile, FileMode.Create, FileAccess.Write, FileShare.None))
            await chunkFs.WriteAsync(chunkBytes, 0, chunkBytes.Length, cancellationToken);

        if (isNewChunk)
            session.BytesUploaded += chunkBytes.Length;

        SaveSessionJson(sessionDir, session);

        StorageTelemetry.RecordResumableChunk(ProviderName, chunkBytes.Length);
        activity?.SetStatus(ActivityStatusCode.Ok);

        var isReady = session.BytesUploaded >= session.TotalSize;
        return StorageResult<ChunkUploadResult>.Success(new ChunkUploadResult
        {
            UploadId = request.UploadId,
            BytesUploaded = session.BytesUploaded,
            TotalSize = session.TotalSize,
            IsReadyToComplete = isReady
        });
    }

    public Task<StorageResult<ResumableUploadStatus>> GetUploadStatusAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
    {
        var sessionDir = GetSessionDir(uploadId);
        if (!Directory.Exists(sessionDir))
            return Task.FromResult(StorageResult<ResumableUploadStatus>.Failure(
                $"Session '{uploadId}' not found.", StorageErrorCode.FileNotFound));

        var session = LoadSessionJson(sessionDir);
        if (session is null)
            return Task.FromResult(StorageResult<ResumableUploadStatus>.Failure(
                $"Session '{uploadId}' not found.", StorageErrorCode.FileNotFound));

        return Task.FromResult(StorageResult<ResumableUploadStatus>.Success(new ResumableUploadStatus
        {
            UploadId = uploadId,
            Path = session.Path,
            TotalSize = session.TotalSize,
            BytesUploaded = session.BytesUploaded,
            IsComplete = session.IsComplete,
            IsAborted = session.IsAborted,
            ExpiresAt = session.ExpiresAt
        }));
    }

    public async Task<StorageResult<UploadResult>> CompleteResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.complete", ProviderName, uploadId);

        var sessionDir = GetSessionDir(uploadId);
        if (!Directory.Exists(sessionDir))
        {
            StorageTelemetry.RecordError(ProviderName, "resumable.complete");
            activity?.SetStatus(ActivityStatusCode.Error, "Session not found");
            return StorageResult<UploadResult>.Failure($"Session '{uploadId}' not found.", StorageErrorCode.FileNotFound);
        }

        var session = LoadSessionJson(sessionDir);
        if (session is null)
        {
            StorageTelemetry.RecordError(ProviderName, "resumable.complete");
            return StorageResult<UploadResult>.Failure($"Session '{uploadId}' not found.", StorageErrorCode.FileNotFound);
        }

        if (session.IsAborted)
        {
            StorageTelemetry.RecordError(ProviderName, "resumable.complete");
            activity?.SetStatus(ActivityStatusCode.Error, "Session aborted");
            return StorageResult<UploadResult>.Failure("Session has been aborted.", StorageErrorCode.ValidationFailed);
        }

        // Assemble all chunks in offset order
        var chunkFiles = Directory.GetFiles(sessionDir, "*.chunk")
            .Select(f => (Path: f, Offset: ParseChunkOffset(f)))
            .OrderBy(t => t.Offset)
            .ToList();

        var resolvedPath = ResolvePath(session.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);

        using (var outFs = new FileStream(resolvedPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            foreach (var chunk in chunkFiles)
            {
                byte[] chunkBytes;
                using (var chunkFs = new FileStream(chunk.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    chunkBytes = new byte[chunkFs.Length];
                    var read = 0;
                    while (read < chunkBytes.Length)
                    {
                        var n = await chunkFs.ReadAsync(chunkBytes, read, chunkBytes.Length - read, cancellationToken);
                        if (n == 0) break;
                        read += n;
                    }
                }
                await outFs.WriteAsync(chunkBytes, 0, chunkBytes.Length, cancellationToken);
            }
        }

        // Compute ETag
        string eTag;
        using (var hashFs = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            using var md5 = MD5.Create();
            eTag = Convert.ToBase64String(md5.ComputeHash(hashFs));
        }

        var info = new FileInfo(resolvedPath);

        // Write sidecar metadata
        if (session.ContentType is not null || session.Metadata is not null)
        {
            var meta = new Dictionary<string, string>(StringComparer.Ordinal);
            if (session.ContentType is not null)
                meta["content-type"] = session.ContentType;
            if (session.Metadata is not null)
                foreach (var kvp in session.Metadata)
                    meta[kvp.Key] = kvp.Value;
            await WriteSidecarAsync(resolvedPath, meta, cancellationToken);
        }

        // Clean up session directory
        Directory.Delete(sessionDir, recursive: true);

        StorageTelemetry.RecordResumableCompleted(ProviderName);
        activity?.SetStatus(ActivityStatusCode.Ok);

        return StorageResult<UploadResult>.Success(new UploadResult
        {
            Path = session.Path,
            ETag = eTag,
            SizeBytes = info.Length
        });
    }

    public Task<StorageResult> AbortResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.abort", ProviderName, uploadId);

        var sessionDir = GetSessionDir(uploadId);
        if (Directory.Exists(sessionDir))
            Directory.Delete(sessionDir, recursive: true);

        StorageTelemetry.RecordResumableAborted(ProviderName);
        activity?.SetStatus(ActivityStatusCode.Ok);

        return Task.FromResult(StorageResult.Success());
    }

    // ─── Resumable helpers ────────────────────────────────────────────────────

    private string GetSessionDir(string uploadId)
    {
        var basePath = Path.GetFullPath(_options.BasePath);
        return Path.Combine(basePath, ".resumable", uploadId);
    }

    private static void SaveSessionJson(string sessionDir, ResumableUploadSession session)
    {
        var sessionFile = Path.Combine(sessionDir, "session.json");
        var json = JsonSerializer.Serialize(session);
        File.WriteAllText(sessionFile, json, Encoding.UTF8);
    }

    private static ResumableUploadSession? LoadSessionJson(string sessionDir)
    {
        var sessionFile = Path.Combine(sessionDir, "session.json");
        if (!File.Exists(sessionFile))
            return null;

        try
        {
            var json = File.ReadAllText(sessionFile);
            return JsonSerializer.Deserialize<ResumableUploadSession>(json);
        }
        catch
        {
            return null;
        }
    }

    private static long ParseChunkOffset(string chunkFilePath)
    {
        var name = Path.GetFileNameWithoutExtension(chunkFilePath);
        return long.TryParse(name, out var offset) ? offset : 0;
    }

    // ─── IPresignedUrlProvider ────────────────────────────────────────────────

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
