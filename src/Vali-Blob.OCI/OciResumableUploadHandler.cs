using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Oci.ObjectstorageService;
using Oci.ObjectstorageService.Requests;
using Oci.ObjectstorageService.Models;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Resumable;
using ValiBlob.Core.Telemetry;

namespace ValiBlob.OCI;

internal sealed class OciResumableUploadHandler
{
    private readonly ObjectStorageClient _client;
    private readonly OCIStorageOptions _options;
    private readonly IResumableSessionStore _sessionStore;
    private readonly ResumableUploadOptions _resumableOptions;
    private readonly ILogger _logger;
    private const string ProviderName = nameof(StorageProviderType.OCI);

    internal OciResumableUploadHandler(
        ObjectStorageClient client,
        OCIStorageOptions options,
        IResumableSessionStore sessionStore,
        ResumableUploadOptions resumableOptions,
        ILogger logger)
    {
        _client = client;
        _options = options;
        _sessionStore = sessionStore;
        _resumableOptions = resumableOptions;
        _logger = logger;
    }

    internal async Task<StorageResult<ResumableUploadSession>> StartAsync(
        ResumableUploadRequest request, CancellationToken cancellationToken)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.start", ProviderName, request.Path);
        try
        {
            var bucket = request.BucketOverride ?? _options.Bucket;
            var createResponse = await _client.CreateMultipartUpload(new CreateMultipartUploadRequest
            {
                NamespaceName = _options.Namespace,
                BucketName = bucket,
                CreateMultipartUploadDetails = new CreateMultipartUploadDetails
                {
                    Object = request.Path,
                    ContentType = request.ContentType
                }
            });

            var ociUploadId = createResponse.MultipartUpload.UploadId;
            var expiration = request.Options?.SessionExpiration ?? _resumableOptions.SessionExpiration;

            var session = new ResumableUploadSession
            {
                UploadId = Guid.NewGuid().ToString("N"),
                Path = request.Path,
                BucketOverride = request.BucketOverride,
                TotalSize = request.TotalSize,
                BytesUploaded = 0,
                ContentType = request.ContentType,
                Metadata = request.Metadata,
                ExpiresAt = DateTimeOffset.UtcNow.Add(expiration),
                ProviderData = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ociUploadId"] = ociUploadId,
                    ["ociNamespace"] = _options.Namespace,
                    ["ociBucket"] = bucket,
                    ["ociObjectName"] = request.Path,
                    ["ociNextPartNum"] = "1",
                    ["ociParts"] = string.Empty
                }
            };

            await _sessionStore.SaveAsync(session, cancellationToken);
            _logger.LogInformation("[OCI] Started multipart upload session {UploadId} for {Path}", session.UploadId, session.Path);
            StorageTelemetry.RecordResumableStarted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult<ResumableUploadSession>.Success(session);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.start");
            _logger.LogError(ex, "[OCI] Failed to start multipart upload for {Path}", request.Path);
            return StorageResult<ResumableUploadSession>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    internal async Task<StorageResult<ChunkUploadResult>> UploadChunkAsync(
        ResumableChunkRequest request, CancellationToken cancellationToken)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.chunk", ProviderName, request.UploadId);
        try
        {
            var session = await _sessionStore.GetAsync(request.UploadId, cancellationToken);
            if (session is null)
            {
                StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
                activity?.SetStatus(ActivityStatusCode.Error, "Session not found");
                return StorageResult<ChunkUploadResult>.Failure(
                    $"Upload session '{request.UploadId}' not found or expired.", StorageErrorCode.FileNotFound);
            }
            if (session.IsAborted)
            {
                StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
                activity?.SetStatus(ActivityStatusCode.Error, "Session aborted");
                return StorageResult<ChunkUploadResult>.Failure(
                    "Upload session has been aborted.", StorageErrorCode.ValidationFailed);
            }

            var partNumber = int.Parse(session.ProviderData["ociNextPartNum"]);
            var chunkBytes = await StreamReadHelper.ReadChunkAsync(request.Data, request.Length, cancellationToken)
                .ConfigureAwait(false);
            var chunkMd5 = ChunkChecksumHelper.ComputeMd5Base64(chunkBytes);

            if (request.ExpectedMd5 is not null)
            {
                var error = ChunkChecksumHelper.Validate(chunkMd5, request.ExpectedMd5);
                if (error is not null)
                {
                    StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
                    activity?.SetStatus(ActivityStatusCode.Error, error);
                    return StorageResult<ChunkUploadResult>.Failure(error, StorageErrorCode.ValidationFailed);
                }
            }

            using var chunkStream = new MemoryStream(chunkBytes);
            var partResponse = await _client.UploadPart(new UploadPartRequest
            {
                NamespaceName = session.ProviderData["ociNamespace"],
                BucketName = session.ProviderData["ociBucket"],
                ObjectName = session.ProviderData["ociObjectName"],
                UploadId = session.ProviderData["ociUploadId"],
                UploadPartNum = partNumber,
                UploadPartBody = chunkStream,
                ContentLength = chunkBytes.Length,
                ContentMD5 = _resumableOptions.EnableChecksumValidation ? chunkMd5 : null
            });

            var etag = partResponse.ETag?.Trim('"') ?? string.Empty;
            var partsEntry = $"{partNumber}:{etag}";
            var existing = session.ProviderData["ociParts"];
            session.ProviderData["ociParts"] = string.IsNullOrEmpty(existing) ? partsEntry : $"{existing}|{partsEntry}";
            session.ProviderData["ociNextPartNum"] = (partNumber + 1).ToString();
            session.BytesUploaded += chunkBytes.Length;

            await _sessionStore.UpdateAsync(session, cancellationToken);
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
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
            _logger.LogError(ex, "[OCI] Chunk upload failed for session {UploadId}", request.UploadId);
            return StorageResult<ChunkUploadResult>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    internal async Task<StorageResult<UploadResult>> CompleteAsync(
        string uploadId, CancellationToken cancellationToken)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.complete", ProviderName, uploadId);
        try
        {
            var session = await _sessionStore.GetAsync(uploadId, cancellationToken);
            if (session is null)
            {
                StorageTelemetry.RecordError(ProviderName, "resumable.complete");
                activity?.SetStatus(ActivityStatusCode.Error, "Session not found");
                return StorageResult<UploadResult>.Failure(
                    $"Upload session '{uploadId}' not found or expired.", StorageErrorCode.FileNotFound);
            }
            if (session.IsAborted)
            {
                StorageTelemetry.RecordError(ProviderName, "resumable.complete");
                activity?.SetStatus(ActivityStatusCode.Error, "Session aborted");
                return StorageResult<UploadResult>.Failure(
                    "Upload session has been aborted.", StorageErrorCode.ValidationFailed);
            }

            var parts = ParseOciParts(session.ProviderData["ociParts"]);
            await _client.CommitMultipartUpload(new CommitMultipartUploadRequest
            {
                NamespaceName = session.ProviderData["ociNamespace"],
                BucketName = session.ProviderData["ociBucket"],
                ObjectName = session.ProviderData["ociObjectName"],
                UploadId = session.ProviderData["ociUploadId"],
                CommitMultipartUploadDetails = new CommitMultipartUploadDetails
                {
                    PartsToCommit = parts
                }
            });

            session.IsComplete = true;
            await _sessionStore.DeleteAsync(uploadId, cancellationToken);

            _logger.LogInformation("[OCI] Completed multipart upload session {UploadId} for {Path}", uploadId, session.Path);
            StorageTelemetry.RecordResumableCompleted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult<UploadResult>.Success(new UploadResult
            {
                Path = session.Path,
                SizeBytes = session.BytesUploaded
            });
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.complete");
            _logger.LogError(ex, "[OCI] CompleteResumableUpload failed for session {UploadId}", uploadId);
            return StorageResult<UploadResult>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    internal async Task<StorageResult> AbortAsync(string uploadId, CancellationToken cancellationToken)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.abort", ProviderName, uploadId);
        try
        {
            var session = await _sessionStore.GetAsync(uploadId, cancellationToken);
            if (session is null)
            {
                StorageTelemetry.RecordError(ProviderName, "resumable.abort");
                activity?.SetStatus(ActivityStatusCode.Error, "Session not found");
                return StorageResult.Failure(
                    $"Upload session '{uploadId}' not found or expired.", StorageErrorCode.FileNotFound);
            }

            await _client.AbortMultipartUpload(new AbortMultipartUploadRequest
            {
                NamespaceName = session.ProviderData["ociNamespace"],
                BucketName = session.ProviderData["ociBucket"],
                ObjectName = session.ProviderData["ociObjectName"],
                UploadId = session.ProviderData["ociUploadId"]
            });

            await _sessionStore.DeleteAsync(uploadId, cancellationToken);
            _logger.LogInformation("[OCI] Aborted multipart upload session {UploadId}", uploadId);
            StorageTelemetry.RecordResumableAborted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult.Success();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.abort");
            _logger.LogError(ex, "[OCI] AbortResumableUpload failed for session {UploadId}", uploadId);
            return StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    private static List<CommitMultipartUploadPartDetails> ParseOciParts(string raw)
    {
        var parts = new List<CommitMultipartUploadPartDetails>();
        if (string.IsNullOrEmpty(raw)) return parts;
        foreach (var entry in raw.Split('|'))
        {
            var idx = entry.IndexOf(':');
            if (idx < 0) continue;
            var num = int.Parse(entry.Substring(0, idx));
            var etag = entry.Substring(idx + 1);
            parts.Add(new CommitMultipartUploadPartDetails { PartNum = num, Etag = etag });
        }
        return parts;
    }
}
