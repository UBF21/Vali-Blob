using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oci.Common.Auth;
using Oci.ObjectstorageService;
using Oci.ObjectstorageService.Requests;
using Oci.ObjectstorageService.Models;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Providers;
using ValiBlob.Core.Resumable;
using ValiBlob.Core.Telemetry;

namespace ValiBlob.OCI;

public sealed class OCIStorageProvider : BaseStorageProvider, IResumableUploadProvider, IPresignedUrlProvider
{
    private readonly ObjectStorageClient _client;
    private readonly OCIStorageOptions _options;
    private readonly IResumableSessionStore _sessionStore;
    private readonly ResumableUploadOptions _resumableOptions;

    public OCIStorageProvider(
        ObjectStorageClient client,
        IOptions<OCIStorageOptions> options,
        ILogger<OCIStorageProvider> logger,
        IOptions<ResilienceOptions> resilienceOptions,
        IOptions<EncryptionOptions> encryptionOptions,
        StoragePipelineBuilder pipeline,
        IResumableSessionStore sessionStore,
        IOptions<ResumableUploadOptions> resumableOptions)
        : base(logger, resilienceOptions, encryptionOptions, pipeline)
    {
        _client = client;
        _options = options.Value;
        _sessionStore = sessionStore;
        _resumableOptions = resumableOptions.Value;
    }

    public override string ProviderName => "OCI";

    private string GetServiceBaseUrl() =>
        _options.ServiceUrl ?? $"https://objectstorage.{_options.Region}.oraclecloud.com";

    protected override async Task<StorageResult<UploadResult>> UploadCoreAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var bucket = ResolveBucket(request.BucketOverride, _options.Bucket);
        var putRequest = new PutObjectRequest
        {
            NamespaceName = _options.Namespace,
            BucketName = bucket,
            ObjectName = request.Path,
            PutObjectBody = request.Content,
            ContentType = request.ContentType,
            ContentLength = request.ContentLength
        };

        var response = await _client.PutObject(putRequest);

        return StorageResult<UploadResult>.Success(new UploadResult
        {
            Path = request.Path,
            ETag = response.ETag,
            SizeBytes = request.ContentLength ?? 0
        });
    }

    protected override async Task<StorageResult<Stream>> DownloadCoreAsync(
        DownloadRequest request, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucket(request.BucketOverride, _options.Bucket);
        var getRequest = new GetObjectRequest
        {
            NamespaceName = _options.Namespace,
            BucketName = bucket,
            ObjectName = request.Path
        };

        if (request.Range is not null)
        {
            getRequest.Range = new Oci.Common.Model.Range
            {
                StartByte = request.Range.From,
                EndByte = request.Range.To
            };
        }

        var response = await _client.GetObject(getRequest);
        return StorageResult<Stream>.Success(response.InputStream);
    }

    protected override async Task<StorageResult> DeleteCoreAsync(string path, CancellationToken cancellationToken)
    {
        await _client.DeleteObject(new DeleteObjectRequest
        {
            NamespaceName = _options.Namespace,
            BucketName = _options.Bucket,
            ObjectName = path
        });
        return StorageResult.Success();
    }

    protected override async Task<StorageResult<bool>> ExistsCoreAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await _client.HeadObject(new HeadObjectRequest
            {
                NamespaceName = _options.Namespace,
                BucketName = _options.Bucket,
                ObjectName = path
            });
            return StorageResult<bool>.Success(true);
        }
        catch (Oci.Common.Model.OciException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return StorageResult<bool>.Success(false);
        }
    }

    protected override Task<StorageResult<string>> GetUrlCoreAsync(string path, CancellationToken cancellationToken)
    {
        var url = _options.CdnBaseUrl is not null
            ? $"{_options.CdnBaseUrl.TrimEnd('/')}/{path}"
            : $"{GetServiceBaseUrl()}/n/{_options.Namespace}/b/{_options.Bucket}/o/{path}";

        return Task.FromResult(StorageResult<string>.Success(url));
    }

    protected override async Task<StorageResult> CopyCoreAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await _client.CopyObject(new CopyObjectRequest
        {
            NamespaceName = _options.Namespace,
            BucketName = _options.Bucket,
            CopyObjectDetails = new CopyObjectDetails
            {
                SourceObjectName = sourcePath,
                DestinationBucket = _options.Bucket,
                DestinationNamespace = _options.Namespace,
                DestinationObjectName = destinationPath,
                DestinationRegion = _options.Region
            }
        });
        return StorageResult.Success();
    }

    protected override async Task<StorageResult<FileMetadata>> GetMetadataCoreAsync(string path, CancellationToken cancellationToken)
    {
        var response = await _client.HeadObject(new HeadObjectRequest
        {
            NamespaceName = _options.Namespace,
            BucketName = _options.Bucket,
            ObjectName = path
        });

        return StorageResult<FileMetadata>.Success(new FileMetadata
        {
            Path = path,
            SizeBytes = response.ContentLength ?? 0,
            ContentType = response.ContentType,
            LastModified = response.LastModified,
            ETag = response.ETag
        });
    }

    protected override Task<StorageResult> SetMetadataCoreAsync(string path, IDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        // OCI requires re-upload to set metadata
        Logger.LogWarning("[OCI] SetMetadata requires re-upload in OCI Object Storage.");
        return Task.FromResult(StorageResult.Failure("OCI requires re-upload to update metadata.", StorageErrorCode.NotSupported));
    }

    // ─── IResumableUploadProvider ─────────────────────────────────────────────

    public async Task<StorageResult<ResumableUploadSession>> StartResumableUploadAsync(
        ResumableUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.start", ProviderName, request.Path);
        try
        {
            var bucket = ResolveBucket(request.BucketOverride, _options.Bucket);
            var createRequest = new CreateMultipartUploadRequest
            {
                NamespaceName = _options.Namespace,
                BucketName = bucket,
                CreateMultipartUploadDetails = new CreateMultipartUploadDetails
                {
                    Object = request.Path,
                    ContentType = request.ContentType
                }
            };

            var createResponse = await _client.CreateMultipartUpload(createRequest);
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
                    ["ociParts"] = string.Empty  // "partNum:eTag|..." accumulated list
                }
            };

            await _sessionStore.SaveAsync(session, cancellationToken);
            Logger.LogInformation("[OCI] Started multipart upload session {UploadId} for {Path}", session.UploadId, session.Path);
            StorageTelemetry.RecordResumableStarted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult<ResumableUploadSession>.Success(session);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.start");
            Logger.LogError(ex, "[OCI] Failed to start multipart upload for {Path}", request.Path);
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

            var partNumber = int.Parse(session.ProviderData["ociNextPartNum"]);

            var chunkBytes = await StreamReadHelper.ReadChunkAsync(request.Data, request.Length, cancellationToken)
                .ConfigureAwait(false);

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
                NamespaceName = session.ProviderData["ociNamespace"],
                BucketName = session.ProviderData["ociBucket"],
                ObjectName = session.ProviderData["ociObjectName"],
                UploadId = session.ProviderData["ociUploadId"],
                UploadPartNum = partNumber,
                UploadPartBody = chunkStream,
                ContentLength = chunkBytes.Length,
                ContentMD5 = _resumableOptions.EnableChecksumValidation ? chunkMd5 : null
            };

            var partResponse = await _client.UploadPart(uploadPartRequest);
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
            Logger.LogError(ex, "[OCI] Chunk upload failed for session {UploadId}", request.UploadId);
            return StorageResult<ChunkUploadResult>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    protected override IResumableSessionStore GetSessionStore() => _sessionStore;

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

            var parts = ParseOCIParts(session.ProviderData["ociParts"]);
            var commitRequest = new CommitMultipartUploadRequest
            {
                NamespaceName = session.ProviderData["ociNamespace"],
                BucketName = session.ProviderData["ociBucket"],
                ObjectName = session.ProviderData["ociObjectName"],
                UploadId = session.ProviderData["ociUploadId"],
                CommitMultipartUploadDetails = new CommitMultipartUploadDetails
                {
                    PartsToCommit = parts
                }
            };

            await _client.CommitMultipartUpload(commitRequest);
            session.IsComplete = true;
            await _sessionStore.DeleteAsync(uploadId, cancellationToken);

            Logger.LogInformation("[OCI] Completed multipart upload session {UploadId} for {Path}", uploadId, session.Path);
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
            Logger.LogError(ex, "[OCI] CompleteResumableUpload failed for session {UploadId}", uploadId);
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

            await _client.AbortMultipartUpload(new AbortMultipartUploadRequest
            {
                NamespaceName = session.ProviderData["ociNamespace"],
                BucketName = session.ProviderData["ociBucket"],
                ObjectName = session.ProviderData["ociObjectName"],
                UploadId = session.ProviderData["ociUploadId"]
            });

            await _sessionStore.DeleteAsync(uploadId, cancellationToken);
            Logger.LogInformation("[OCI] Aborted multipart upload session {UploadId}", uploadId);
            StorageTelemetry.RecordResumableAborted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult.Success();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.abort");
            Logger.LogError(ex, "[OCI] AbortResumableUpload failed for session {UploadId}", uploadId);
            return StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    // ─── IPresignedUrlProvider (via Pre-Authenticated Requests) ──────────────

    public async Task<StorageResult<string>> GetPresignedUploadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.CreatePreauthenticatedRequest(
                new CreatePreauthenticatedRequestRequest
                {
                    NamespaceName = _options.Namespace,
                    BucketName = _options.Bucket,
                    CreatePreauthenticatedRequestDetails = new CreatePreauthenticatedRequestDetails
                    {
                        Name = $"upload-{Guid.NewGuid():N}",
                        ObjectName = path,
                        AccessType = CreatePreauthenticatedRequestDetails.AccessTypeEnum.ObjectWrite,
                        TimeExpires = DateTime.UtcNow.Add(expiration)
                    }
                });

            var url = $"{GetServiceBaseUrl()}{response.PreauthenticatedRequest.AccessUri}";
            return StorageResult<string>.Success(url);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[OCI] GetPresignedUploadUrl failed for {Path}", path);
            return StorageResult<string>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    public async Task<StorageResult<string>> GetPresignedDownloadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.CreatePreauthenticatedRequest(
                new CreatePreauthenticatedRequestRequest
                {
                    NamespaceName = _options.Namespace,
                    BucketName = _options.Bucket,
                    CreatePreauthenticatedRequestDetails = new CreatePreauthenticatedRequestDetails
                    {
                        Name = $"download-{Guid.NewGuid():N}",
                        ObjectName = path,
                        AccessType = CreatePreauthenticatedRequestDetails.AccessTypeEnum.ObjectRead,
                        TimeExpires = DateTime.UtcNow.Add(expiration)
                    }
                });

            var url = $"{GetServiceBaseUrl()}{response.PreauthenticatedRequest.AccessUri}";
            return StorageResult<string>.Success(url);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[OCI] GetPresignedDownloadUrl failed for {Path}", path);
            return StorageResult<string>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    private static List<CommitMultipartUploadPartDetails> ParseOCIParts(string raw)
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

    protected override async Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesCoreAsync(
        string? prefix, ListOptions? options, CancellationToken cancellationToken)
    {
        var response = await _client.ListObjects(new ListObjectsRequest
        {
            NamespaceName = _options.Namespace,
            BucketName = _options.Bucket,
            Prefix = prefix,
            Limit = options?.MaxResults ?? 1000
        });

        var entries = response.ListObjects.Objects.Select(o => new FileEntry
        {
            Path = o.Name,
            SizeBytes = o.Size ?? 0,
            LastModified = o.TimeModified,
            ETag = o.Etag
        }).ToList();

        return StorageResult<IReadOnlyList<FileEntry>>.Success(entries.AsReadOnly());
    }
}
