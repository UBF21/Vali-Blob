using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ValiBlob.Core.Exceptions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Resumable;
using ValiBlob.Core.Options;
using ValiBlob.Core.Telemetry;

namespace ValiBlob.Local;

internal sealed class LocalStorageResumableHandler
{
    private readonly string _basePath;
    private readonly ILogger _logger;
    private const string ProviderName = nameof(StorageProviderType.Local);

    internal LocalStorageResumableHandler(string basePath, ILogger logger)
    {
        _basePath = basePath;
        _logger = logger;
    }

    internal Task<StorageResult<ResumableUploadSession>> StartAsync(
        ResumableUploadRequest request,
        CancellationToken cancellationToken)
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

    internal async Task<StorageResult<ChunkUploadResult>> UploadChunkAsync(
        ResumableChunkRequest request,
        CancellationToken cancellationToken)
    {
        ValidateUploadId(request.UploadId);
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

        var validationError = ValidateChunkRequest(request, session);
        if (validationError is not null)
        {
            StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
            activity?.SetStatus(ActivityStatusCode.Error, validationError);
            return StorageResult<ChunkUploadResult>.Failure(validationError, StorageErrorCode.ValidationFailed);
        }

        var chunkBytes = await ReadChunkBytesAsync(request, cancellationToken);

        if (request.ExpectedMd5 is not null)
        {
            var actualMd5 = ChunkChecksumHelper.ComputeMd5Base64(chunkBytes);
            var checksumError = ChunkChecksumHelper.Validate(actualMd5, request.ExpectedMd5);
            if (checksumError is not null)
            {
                StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
                activity?.SetStatus(ActivityStatusCode.Error, checksumError);
                return StorageResult<ChunkUploadResult>.Failure(checksumError, StorageErrorCode.ValidationFailed);
            }
        }

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

    internal async Task<StorageResult<UploadResult>> CompleteAsync(
        string uploadId,
        CancellationToken cancellationToken)
    {
        ValidateUploadId(uploadId);
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

        var chunkFiles = Directory.GetFiles(sessionDir, "*.chunk")
            .Select(f => (Path: f, Offset: ParseChunkOffset(f)))
            .OrderBy(t => t.Offset)
            .ToList();

        var resolvedPath = LocalStoragePathHelper.ResolvePath(_basePath, session.Path);
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

        var eTag = await LocalStorageSidecarHelper.ComputeETagAsync(resolvedPath, cancellationToken);
        var info = new FileInfo(resolvedPath);

        if (session.ContentType is not null || session.Metadata is not null)
        {
            var meta = new Dictionary<string, string>(StringComparer.Ordinal);
            if (session.ContentType is not null)
                meta["content-type"] = session.ContentType;
            if (session.Metadata is not null)
                foreach (var kvp in session.Metadata)
                    meta[kvp.Key] = kvp.Value;
            await LocalStorageSidecarHelper.WriteAsync(resolvedPath, meta, cancellationToken);
        }

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

    internal Task<StorageResult> AbortAsync(string uploadId, CancellationToken cancellationToken)
    {
        ValidateUploadId(uploadId);
        using var activity = StorageTelemetry.StartActivity("resumable.abort", ProviderName, uploadId);

        var sessionDir = GetSessionDir(uploadId);
        if (Directory.Exists(sessionDir))
            Directory.Delete(sessionDir, recursive: true);

        StorageTelemetry.RecordResumableAborted(ProviderName);
        activity?.SetStatus(ActivityStatusCode.Ok);

        return Task.FromResult(StorageResult.Success());
    }

    internal Task<StorageResult<ResumableUploadStatus>> GetStatusAsync(
        string uploadId,
        CancellationToken cancellationToken)
    {
        ValidateUploadId(uploadId);
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

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static void ValidateUploadId(string uploadId)
    {
        if (!Guid.TryParse(uploadId, out _))
            throw new StorageValidationException(new[] { $"Invalid uploadId format: '{uploadId}'" });
    }

    private string GetSessionDir(string uploadId)
    {
        var normalizedBase = Path.GetFullPath(_basePath);
        var resumableRoot = Path.Combine(normalizedBase, ".resumable");
        var sessionDir = Path.GetFullPath(Path.Combine(resumableRoot, uploadId));

        if (!sessionDir.StartsWith(resumableRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sessionDir, resumableRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Invalid uploadId: '{uploadId}'");
        }

        return sessionDir;
    }

    private static void SaveSessionJson(string sessionDir, ResumableUploadSession session)
    {
        var sessionFile = Path.Combine(sessionDir, "session.json");
        var json = System.Text.Json.JsonSerializer.Serialize(session);
        File.WriteAllText(sessionFile, json, System.Text.Encoding.UTF8);
    }

    private static ResumableUploadSession? LoadSessionJson(string sessionDir)
    {
        var sessionFile = Path.Combine(sessionDir, "session.json");
        if (!File.Exists(sessionFile))
            return null;

        try
        {
            var json = File.ReadAllText(sessionFile);
            return System.Text.Json.JsonSerializer.Deserialize<ResumableUploadSession>(json);
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

    private static string? ValidateChunkRequest(ResumableChunkRequest request, ResumableUploadSession session)
    {
        if (session.IsAborted)
            return "Session has been aborted.";
        if (request.Offset < 0)
            return $"Chunk offset must be non-negative, got {request.Offset}.";
        if (session.TotalSize > 0 && request.Offset >= session.TotalSize)
            return $"Chunk offset {request.Offset} exceeds total file size {session.TotalSize}.";
        if (request.Length.HasValue && request.Length.Value <= 0)
            return $"Chunk length must be positive, got {request.Length.Value}.";
        return null;
    }

    private static async Task<byte[]> ReadChunkBytesAsync(
        ResumableChunkRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Length.HasValue)
        {
            var chunkBytes = new byte[request.Length.Value];
            var read = 0;
            while (read < chunkBytes.Length)
            {
                var n = await request.Data.ReadAsync(chunkBytes, read, chunkBytes.Length - read, cancellationToken);
                if (n == 0) break;
                read += n;
            }
            if (read < chunkBytes.Length) Array.Resize(ref chunkBytes, read);
            return chunkBytes;
        }
        else
        {
            using var ms = new MemoryStream();
            await request.Data.CopyToAsync(ms, 81920);
            return ms.ToArray();
        }
    }
}
