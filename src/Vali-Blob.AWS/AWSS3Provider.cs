using System.Collections.Generic;
using System.Diagnostics;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Providers;
using ValiBlob.Core.Resumable;
using ValiBlob.Core.Telemetry;

namespace ValiBlob.AWS;

public sealed class AWSS3Provider : BaseStorageProvider, IPresignedUrlProvider, IResumableUploadProvider
{
    private readonly IAmazonS3 _s3Client;
    private readonly AWSS3Options _options;
    private readonly IResumableSessionStore _sessionStore;
    private readonly ResumableUploadOptions _resumableOptions;

    public AWSS3Provider(
        IAmazonS3 s3Client,
        IOptions<AWSS3Options> options,
        ILogger<AWSS3Provider> logger,
        IOptions<ResilienceOptions> resilienceOptions,
        IOptions<EncryptionOptions> encryptionOptions,
        StoragePipelineBuilder pipeline,
        IResumableSessionStore sessionStore,
        IOptions<ResumableUploadOptions> resumableOptions)
        : base(logger, resilienceOptions, encryptionOptions, pipeline)
    {
        _s3Client = s3Client;
        _options = options.Value;
        _sessionStore = sessionStore;
        _resumableOptions = resumableOptions.Value;
    }

    public override string ProviderName => "AWS";

    protected override async Task<StorageResult<UploadResult>> UploadCoreAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var bucket = ResolveBucket(request.BucketOverride, _options.Bucket);
        var shouldMultipart = request.Options?.UseMultipart == true ||
                              (request.ContentLength ?? 0) > _options.MultipartThresholdMb * 1024L * 1024;

        if (shouldMultipart)
            return await UploadMultipartAsync(request, bucket, progress, cancellationToken);

        var putRequest = new PutObjectRequest
        {
            BucketName = bucket,
            Key = request.Path,
            InputStream = request.Content,
            ContentType = request.ContentType,
            AutoCloseStream = false
        };

        if (request.Metadata is not null)
        {
            foreach (var kvp in request.Metadata)
                putRequest.Metadata[$"x-amz-meta-{kvp.Key}"] = kvp.Value;
        }

        putRequest.StreamTransferProgress += (_, args) =>
            progress?.Report(new UploadProgress(args.TransferredBytes, args.TotalBytes));

        var response = await _s3Client.PutObjectAsync(putRequest, cancellationToken);

        return StorageResult<UploadResult>.Success(new UploadResult
        {
            Path = request.Path,
            ETag = response.ETag,
            SizeBytes = request.ContentLength ?? 0
        });
    }

    private async Task<StorageResult<UploadResult>> UploadMultipartAsync(
        UploadRequest request,
        string bucket,
        IProgress<UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var transferUtility = new TransferUtility(_s3Client);
        var uploadRequest = new TransferUtilityUploadRequest
        {
            BucketName = bucket,
            Key = request.Path,
            InputStream = request.Content,
            ContentType = request.ContentType,
            AutoCloseStream = false,
            PartSize = _options.MultipartChunkSizeMb * 1024L * 1024
        };

        if (request.Metadata is not null)
        {
            foreach (var kvp in request.Metadata)
                uploadRequest.Metadata[$"x-amz-meta-{kvp.Key}"] = kvp.Value;
        }

        uploadRequest.UploadProgressEvent += (_, args) =>
            progress?.Report(new UploadProgress(args.TransferredBytes, args.TotalBytes));

        await transferUtility.UploadAsync(uploadRequest, cancellationToken);

        return StorageResult<UploadResult>.Success(new UploadResult
        {
            Path = request.Path,
            SizeBytes = request.ContentLength ?? 0
        });
    }

    protected override async Task<StorageResult<Stream>> DownloadCoreAsync(
        DownloadRequest request,
        CancellationToken cancellationToken)
    {
        var bucket = ResolveBucket(request.BucketOverride, _options.Bucket);
        var getRequest = new GetObjectRequest
        {
            BucketName = bucket,
            Key = request.Path
        };

        if (request.Range is not null)
        {
            getRequest.ByteRange = request.Range.To.HasValue
                ? new ByteRange(request.Range.From, request.Range.To.Value)
                : new ByteRange($"bytes={request.Range.From}-");
        }

        var response = await _s3Client.GetObjectAsync(getRequest, cancellationToken);
        return StorageResult<Stream>.Success(response.ResponseStream);
    }

    protected override async Task<StorageResult> DeleteCoreAsync(string path, CancellationToken cancellationToken)
    {
        await _s3Client.DeleteObjectAsync(_options.Bucket, path, cancellationToken);
        return StorageResult.Success();
    }

    protected override async Task<StorageResult<bool>> ExistsCoreAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await _s3Client.GetObjectMetadataAsync(_options.Bucket, path, cancellationToken);
            return StorageResult<bool>.Success(true);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return StorageResult<bool>.Success(false);
        }
    }

    protected override Task<StorageResult<string>> GetUrlCoreAsync(string path, CancellationToken cancellationToken)
    {
        var url = _options.CdnBaseUrl is not null
            ? $"{_options.CdnBaseUrl.TrimEnd('/')}/{path}"
            : $"https://{_options.Bucket}.s3.{_options.Region}.amazonaws.com/{path}";

        return Task.FromResult(StorageResult<string>.Success(url));
    }

    protected override async Task<StorageResult> CopyCoreAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await _s3Client.CopyObjectAsync(_options.Bucket, sourcePath, _options.Bucket, destinationPath, cancellationToken);
        return StorageResult.Success();
    }

    protected override async Task<StorageResult<FileMetadata>> GetMetadataCoreAsync(string path, CancellationToken cancellationToken)
    {
        var response = await _s3Client.GetObjectMetadataAsync(_options.Bucket, path, cancellationToken);

        var metadata = new Dictionary<string, string>();
        foreach (var key in response.Metadata.Keys)
            metadata[key] = response.Metadata[key];

        return StorageResult<FileMetadata>.Success(new FileMetadata
        {
            Path = path,
            SizeBytes = response.ContentLength,
            ContentType = response.Headers.ContentType,
            LastModified = response.LastModified,
            ETag = response.ETag,
            CustomMetadata = metadata
        });
    }

    protected override async Task<StorageResult> SetMetadataCoreAsync(string path, IDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        var copyRequest = new CopyObjectRequest
        {
            SourceBucket = _options.Bucket,
            SourceKey = path,
            DestinationBucket = _options.Bucket,
            DestinationKey = path,
            MetadataDirective = S3MetadataDirective.REPLACE
        };

        foreach (var kvp in metadata)
            copyRequest.Metadata[$"x-amz-meta-{kvp.Key}"] = kvp.Value;

        await _s3Client.CopyObjectAsync(copyRequest, cancellationToken);
        return StorageResult.Success();
    }

    protected override async Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesCoreAsync(
        string? prefix, ListOptions? options, CancellationToken cancellationToken)
    {
        var request = new ListObjectsV2Request
        {
            BucketName = _options.Bucket,
            Prefix = prefix,
            MaxKeys = options?.MaxResults ?? 1000,
            ContinuationToken = options?.ContinuationToken,
            Delimiter = options?.Delimiter
        };

        var response = await _s3Client.ListObjectsV2Async(request, cancellationToken);

        var entries = response.S3Objects.Select(o => new FileEntry
        {
            Path = o.Key,
            SizeBytes = o.Size,
            LastModified = o.LastModified,
            ETag = o.ETag
        }).ToList();

        return StorageResult<IReadOnlyList<FileEntry>>.Success(entries.AsReadOnly());
    }

    public override async Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(
        IEnumerable<StoragePath> paths,
        CancellationToken cancellationToken = default)
    {
        var pathList = new List<StoragePath>(paths);
        if (pathList.Count == 0)
            return StorageResult<BatchDeleteResult>.Success(new BatchDeleteResult
            {
                TotalRequested = 0,
                Deleted = 0,
                Failed = 0
            });

        var errors = new List<BatchDeleteError>();
        var deleted = 0;

        // AWS supports deleting up to 1000 objects at once
        const int batchSize = 1000;
        for (var i = 0; i < pathList.Count; i += batchSize)
        {
            var batch = pathList.Skip(i).Take(batchSize).ToList();
            var deleteRequest = new DeleteObjectsRequest
            {
                BucketName = _options.Bucket,
                Objects = batch.Select(p => new KeyVersion { Key = p }).ToList()
            };

            var response = await _s3Client.DeleteObjectsAsync(deleteRequest, cancellationToken);
            deleted += response.DeletedObjects.Count;

            foreach (var error in response.DeleteErrors)
                errors.Add(new BatchDeleteError { Path = error.Key, Reason = error.Message ?? error.Code });
        }

        return StorageResult<BatchDeleteResult>.Success(new BatchDeleteResult
        {
            TotalRequested = pathList.Count,
            Deleted = deleted,
            Failed = errors.Count,
            Errors = errors.AsReadOnly()
        });
    }

    // ─── IResumableUploadProvider ───────────────────────────────────────────

    public async Task<StorageResult<ResumableUploadSession>> StartResumableUploadAsync(
        ResumableUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.start", ProviderName, request.Path);
        try
        {
            var bucket = ResolveBucket(request.BucketOverride, _options.Bucket);
            var initiateRequest = new InitiateMultipartUploadRequest
            {
                BucketName = bucket,
                Key = request.Path,
                ContentType = request.ContentType
            };

            if (request.Metadata is not null)
            {
                foreach (var kvp in request.Metadata)
                    initiateRequest.Metadata[$"x-amz-meta-{kvp.Key}"] = kvp.Value;
            }

            var response = await _s3Client.InitiateMultipartUploadAsync(initiateRequest, cancellationToken);
            var expiration = (request.Options?.SessionExpiration ?? _resumableOptions.SessionExpiration);

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
                    ["awsUploadId"] = response.UploadId,
                    ["awsBucket"] = bucket,
                    ["awsNextPartNum"] = "1",
                    ["awsParts"] = string.Empty  // accumulated as "partNum:eTag|..."
                }
            };

            await _sessionStore.SaveAsync(session, cancellationToken);

            Logger.LogInformation("[AWS] Started resumable upload session {SessionId} for {Path}", session.UploadId, session.Path);
            StorageTelemetry.RecordResumableStarted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult<ResumableUploadSession>.Success(session);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.start");
            Logger.LogError(ex, "[AWS] Failed to start resumable upload for {Path}", request.Path);
            return StorageResult<ResumableUploadSession>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    public async Task<StorageResult<ChunkUploadResult>> UploadChunkAsync(
        ResumableChunkRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.chunk", ProviderName, request.UploadId);
        try
        {
            var session = await _sessionStore.GetAsync(request.UploadId, cancellationToken);
            if (session is null)
            {
                StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
                activity?.SetStatus(ActivityStatusCode.Error, "Session not found");
                return StorageResult<ChunkUploadResult>.Failure($"Upload session '{request.UploadId}' not found or expired.", StorageErrorCode.FileNotFound);
            }
            if (session.IsAborted)
            {
                StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
                activity?.SetStatus(ActivityStatusCode.Error, "Session aborted");
                return StorageResult<ChunkUploadResult>.Failure("Upload session has been aborted.", StorageErrorCode.ValidationFailed);
            }

            var partNumber = int.Parse(session.ProviderData["awsNextPartNum"]);

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
                // Resize if actual read differs
                if (read < chunkBytes.Length)
                    Array.Resize(ref chunkBytes, read);
            }
            else
            {
                using var ms = new MemoryStream();
                await request.Data.CopyToAsync(ms, 81920, cancellationToken);
                chunkBytes = ms.ToArray();
            }

            var chunkMd5 = ChunkChecksumHelper.ComputeMd5Base64(chunkBytes);

            // Validate against caller-supplied checksum if provided
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
            var uploadPartRequest = new UploadPartRequest
            {
                BucketName = session.ProviderData["awsBucket"],
                Key = session.Path,
                UploadId = session.ProviderData["awsUploadId"],
                PartNumber = partNumber,
                InputStream = chunkStream,
                IsLastPart = (session.BytesUploaded + chunkBytes.Length) >= session.TotalSize
            };

            // Send MD5 to S3 for server-side integrity validation
            if (_resumableOptions.EnableChecksumValidation)
                uploadPartRequest.MD5Digest = chunkMd5;

            var partResponse = await _s3Client.UploadPartAsync(uploadPartRequest, cancellationToken);

            // Append etag to parts list
            var partsEntry = $"{partNumber}:{partResponse.ETag.Trim('"')}";
            var existing = session.ProviderData["awsParts"];
            session.ProviderData["awsParts"] = string.IsNullOrEmpty(existing) ? partsEntry : $"{existing}|{partsEntry}";
            session.ProviderData["awsNextPartNum"] = (partNumber + 1).ToString();
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
            Logger.LogError(ex, "[AWS] Chunk upload failed for session {UploadId}", request.UploadId);
            return StorageResult<ChunkUploadResult>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    public async Task<StorageResult<ResumableUploadStatus>> GetUploadStatusAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await _sessionStore.GetAsync(uploadId, cancellationToken);
            if (session is null)
                return StorageResult<ResumableUploadStatus>.Failure($"Upload session '{uploadId}' not found or expired.", StorageErrorCode.FileNotFound);

            return StorageResult<ResumableUploadStatus>.Success(new ResumableUploadStatus
            {
                UploadId = uploadId,
                Path = session.Path,
                TotalSize = session.TotalSize,
                BytesUploaded = session.BytesUploaded,
                IsComplete = session.IsComplete,
                IsAborted = session.IsAborted,
                ExpiresAt = session.ExpiresAt
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[AWS] GetUploadStatus failed for session {UploadId}", uploadId);
            return StorageResult<ResumableUploadStatus>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    public async Task<StorageResult<UploadResult>> CompleteResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.complete", ProviderName, uploadId);
        try
        {
            var session = await _sessionStore.GetAsync(uploadId, cancellationToken);
            if (session is null)
            {
                StorageTelemetry.RecordError(ProviderName, "resumable.complete");
                activity?.SetStatus(ActivityStatusCode.Error, "Session not found");
                return StorageResult<UploadResult>.Failure($"Upload session '{uploadId}' not found or expired.", StorageErrorCode.FileNotFound);
            }
            if (session.IsAborted)
            {
                StorageTelemetry.RecordError(ProviderName, "resumable.complete");
                activity?.SetStatus(ActivityStatusCode.Error, "Session aborted");
                return StorageResult<UploadResult>.Failure("Upload session has been aborted.", StorageErrorCode.ValidationFailed);
            }

            var parts = ParseS3Parts(session.ProviderData["awsParts"]);
            var completeRequest = new CompleteMultipartUploadRequest
            {
                BucketName = session.ProviderData["awsBucket"],
                Key = session.Path,
                UploadId = session.ProviderData["awsUploadId"],
                PartETags = parts
            };

            var response = await _s3Client.CompleteMultipartUploadAsync(completeRequest, cancellationToken);

            session.IsComplete = true;
            await _sessionStore.UpdateAsync(session, cancellationToken);
            await _sessionStore.DeleteAsync(uploadId, cancellationToken);

            Logger.LogInformation("[AWS] Completed resumable upload session {UploadId} for {Path}", uploadId, session.Path);
            StorageTelemetry.RecordResumableCompleted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult<UploadResult>.Success(new UploadResult
            {
                Path = session.Path,
                ETag = response.ETag,
                SizeBytes = session.BytesUploaded
            });
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.complete");
            Logger.LogError(ex, "[AWS] CompleteResumableUpload failed for session {UploadId}", uploadId);
            return StorageResult<UploadResult>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    public async Task<StorageResult> AbortResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.abort", ProviderName, uploadId);
        try
        {
            var session = await _sessionStore.GetAsync(uploadId, cancellationToken);
            if (session is null)
            {
                StorageTelemetry.RecordError(ProviderName, "resumable.abort");
                activity?.SetStatus(ActivityStatusCode.Error, "Session not found");
                return StorageResult.Failure($"Upload session '{uploadId}' not found or expired.", StorageErrorCode.FileNotFound);
            }

            await _s3Client.AbortMultipartUploadAsync(
                session.ProviderData["awsBucket"],
                session.Path,
                session.ProviderData["awsUploadId"],
                cancellationToken);

            await _sessionStore.DeleteAsync(uploadId, cancellationToken);
            Logger.LogInformation("[AWS] Aborted resumable upload session {UploadId}", uploadId);
            StorageTelemetry.RecordResumableAborted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult.Success();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.abort");
            Logger.LogError(ex, "[AWS] AbortResumableUpload failed for session {UploadId}", uploadId);
            return StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    private static List<PartETag> ParseS3Parts(string raw)
    {
        var parts = new List<PartETag>();
        if (string.IsNullOrEmpty(raw)) return parts;
        foreach (var entry in raw.Split('|'))
        {
            var idx = entry.IndexOf(':');
            if (idx < 0) continue;
            var num = int.Parse(entry.Substring(0, idx));
            var etag = entry.Substring(idx + 1);
            parts.Add(new PartETag(num, etag));
        }
        return parts;
    }

    // ─── IPresignedUrlProvider ───────────────────────────────────────────────

    public async Task<StorageResult<string>> GetPresignedUploadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        var url = await _s3Client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = path,
            Expires = DateTime.UtcNow.Add(expiration),
            Verb = HttpVerb.PUT
        });

        return StorageResult<string>.Success(url);
    }

    public async Task<StorageResult<string>> GetPresignedDownloadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        var url = await _s3Client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = path,
            Expires = DateTime.UtcNow.Add(expiration),
            Verb = HttpVerb.GET
        });

        return StorageResult<string>.Success(url);
    }
}
