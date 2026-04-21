using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Providers;
using ValiBlob.Core.Resumable;
using ValiBlob.Core.Telemetry;
using Object = Google.Apis.Storage.v1.Data.Object;

namespace ValiBlob.GCP;

public sealed class GCPStorageProvider : BaseStorageProvider, IResumableUploadProvider, IPresignedUrlProvider
{
    private readonly StorageClient _storageClient;
    private readonly GCPStorageOptions _options;
    private readonly IResumableSessionStore _sessionStore;
    private readonly ResumableUploadOptions _resumableOptions;
    private readonly GCPResumableBuffer _buffer;
    private readonly UrlSigner? _urlSigner;

    public GCPStorageProvider(
        StorageClient storageClient,
        IOptions<GCPStorageOptions> options,
        ILogger<GCPStorageProvider> logger,
        IOptions<ResilienceOptions> resilienceOptions,
        IOptions<EncryptionOptions> encryptionOptions,
        StoragePipelineBuilder pipeline,
        IResumableSessionStore sessionStore,
        IOptions<ResumableUploadOptions> resumableOptions,
        GCPResumableBuffer buffer,
        Func<string, HttpClient> httpClientFactory)
        : base(logger, resilienceOptions, encryptionOptions, pipeline, httpClientFactory)
    {
        _storageClient = storageClient;
        _options = options.Value;
        _sessionStore = sessionStore;
        _resumableOptions = resumableOptions.Value;
        _buffer = buffer;

        // Build UrlSigner from service account credentials if available (required for signed URLs)
        if (_options.CredentialsPath is not null)
            _urlSigner = UrlSigner.FromCredential(GoogleCredential.FromFile(_options.CredentialsPath));
        else if (_options.CredentialsJson is not null)
            _urlSigner = UrlSigner.FromCredential(GoogleCredential.FromJson(_options.CredentialsJson));
        // Application Default Credentials may not support signing — UrlSigner stays null in that case
    }

    public override string ProviderName => "GCP";

    protected override async Task<StorageResult<UploadResult>> UploadCoreAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var bucket = ResolveBucket(request.BucketOverride, _options.Bucket);
        var uploadObject = new Object
        {
            Bucket = bucket,
            Name = request.Path,
            ContentType = request.ContentType
        };

        if (request.Metadata is not null)
        {
            uploadObject.Metadata = request.Metadata.ToDictionary(k => k.Key, v => v.Value);
        }

        IProgress<Google.Apis.Upload.IUploadProgress>? gcpProgress = null;
        if (progress is not null)
        {
            gcpProgress = new Progress<Google.Apis.Upload.IUploadProgress>(p =>
                progress.Report(new UploadProgress(p.BytesSent, request.ContentLength)));
        }

        var result = await _storageClient.UploadObjectAsync(
            uploadObject, request.Content, null, cancellationToken, gcpProgress);

        return StorageResult<UploadResult>.Success(new UploadResult
        {
            Path = request.Path,
            ETag = result.ETag,
            SizeBytes = (long)(result.Size ?? 0)
        });
    }

    protected override async Task<StorageResult<Stream>> DownloadCoreAsync(
        DownloadRequest request, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucket(request.BucketOverride, _options.Bucket);
        var ms = new MemoryStream();
        await _storageClient.DownloadObjectAsync(bucket, request.Path, ms, cancellationToken: cancellationToken);

        if (request.Range is not null)
        {
            var from = request.Range.From;
            var to = request.Range.To.HasValue ? request.Range.To.Value : ms.Length - 1;
            var length = to - from + 1;
            var buffer = new byte[length];
            ms.Position = from;
            var read = await ms.ReadAsync(buffer, 0, (int)length, cancellationToken);
            return StorageResult<Stream>.Success(new MemoryStream(buffer, 0, read));
        }

        ms.Position = 0;
        return StorageResult<Stream>.Success(ms);
    }

    protected override async Task<StorageResult> DeleteCoreAsync(string path, CancellationToken cancellationToken)
    {
        await _storageClient.DeleteObjectAsync(_options.Bucket, path, cancellationToken: cancellationToken);
        return StorageResult.Success();
    }

    protected override async Task<StorageResult<bool>> ExistsCoreAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await _storageClient.GetObjectAsync(_options.Bucket, path, cancellationToken: cancellationToken);
            return StorageResult<bool>.Success(true);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return StorageResult<bool>.Success(false);
        }
    }

    protected override Task<StorageResult<string>> GetUrlCoreAsync(string path, CancellationToken cancellationToken)
    {
        var url = _options.CdnBaseUrl is not null
            ? $"{_options.CdnBaseUrl.TrimEnd('/')}/{path}"
            : $"https://storage.googleapis.com/{_options.Bucket}/{path}";

        return Task.FromResult(StorageResult<string>.Success(url));
    }

    protected override async Task<StorageResult> CopyCoreAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await _storageClient.CopyObjectAsync(_options.Bucket, sourcePath, _options.Bucket, destinationPath, cancellationToken: cancellationToken);
        return StorageResult.Success();
    }

    protected override async Task<StorageResult<FileMetadata>> GetMetadataCoreAsync(string path, CancellationToken cancellationToken)
    {
        var obj = await _storageClient.GetObjectAsync(_options.Bucket, path, cancellationToken: cancellationToken);

        return StorageResult<FileMetadata>.Success(new FileMetadata
        {
            Path = path,
            SizeBytes = (long)(obj.Size ?? 0),
            ContentType = obj.ContentType,
            LastModified = obj.UpdatedDateTimeOffset,
            ETag = obj.ETag,
            CustomMetadata = obj.Metadata ?? new Dictionary<string, string>()
        });
    }

    protected override async Task<StorageResult> SetMetadataCoreAsync(string path, IDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        var obj = await _storageClient.GetObjectAsync(_options.Bucket, path, cancellationToken: cancellationToken);
        obj.Metadata = metadata.ToDictionary(k => k.Key, v => v.Value);
        await _storageClient.UpdateObjectAsync(obj, cancellationToken: cancellationToken);
        return StorageResult.Success();
    }

    // ─── IResumableUploadProvider ─────────────────────────────────────────────
    // GCP SDK does not expose its internal resumable-upload URI publicly.
    // Chunks are buffered in a temp file and uploaded atomically on CompleteAsync.

    /// <summary>
    /// Starts a buffered resumable upload session.
    /// Note: GCP uses local temp file buffering — chunks are not streamed directly to GCS.
    /// The upload is not truly resumable across process restarts; if the process restarts,
    /// the temp file buffer will be lost and the session cannot be continued.
    /// </summary>
    public async Task<StorageResult<ResumableUploadSession>> StartResumableUploadAsync(
        ResumableUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.start", ProviderName, request.Path);
        try
        {
            var uploadId = Guid.NewGuid().ToString("N");
            var tempPath = _buffer.CreateSession(uploadId);
            // Pre-allocate file to TotalSize to allow random-offset writes
            using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                if (request.TotalSize > 0)
                    fs.SetLength(request.TotalSize);
            }

            var expiration = request.Options?.SessionExpiration ?? _resumableOptions.SessionExpiration;
            var session = new ResumableUploadSession
            {
                UploadId = uploadId,
                Path = request.Path,
                BucketOverride = request.BucketOverride,
                TotalSize = request.TotalSize,
                BytesUploaded = 0,
                ContentType = request.ContentType,
                Metadata = request.Metadata,
                ExpiresAt = DateTimeOffset.UtcNow.Add(expiration),
                ProviderData = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["gcpBucket"] = ResolveBucket(request.BucketOverride, _options.Bucket),
                    ["gcpPath"] = request.Path
                }
            };

            await _sessionStore.SaveAsync(session, cancellationToken);
            Logger.LogInformation("[GCP] Started buffered resumable upload session {UploadId} for {Path}", uploadId, request.Path);
            StorageTelemetry.RecordResumableStarted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult<ResumableUploadSession>.Success(session);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.start");
            Logger.LogError(ex, "[GCP] Failed to start resumable upload for {Path}", request.Path);
            return StorageResult<ResumableUploadSession>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    /// <summary>
    /// Writes a chunk to the local temp file buffer for this session.
    /// Note: GCP uses temp file buffering — chunks are not streamed directly to GCS.
    /// The buffer is not durable across process restarts.
    /// </summary>
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

            if (!_buffer.TryGetTempPath(request.UploadId, out var tempPath))
            {
                StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
                activity?.SetStatus(ActivityStatusCode.Error, "Buffer not found");
                return StorageResult<ChunkUploadResult>.Failure("Buffer for upload session not found. Was the process restarted?", StorageErrorCode.ProviderError);
            }

            var chunkBytes = await StreamReadHelper.ReadChunkAsync(request.Data, request.Length, cancellationToken)
                .ConfigureAwait(false);

            // Checksum validation
            if (_resumableOptions.EnableChecksumValidation || request.ExpectedMd5 is not null)
            {
                var actualMd5 = ChunkChecksumHelper.ComputeMd5Base64(chunkBytes);
                if (request.ExpectedMd5 is not null)
                {
                    var error = ChunkChecksumHelper.Validate(actualMd5, request.ExpectedMd5);
                    if (error is not null)
                    {
                        StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
                        activity?.SetStatus(ActivityStatusCode.Error, error);
                        return StorageResult<ChunkUploadResult>.Failure(error, StorageErrorCode.ValidationFailed);
                    }
                }
            }

            // Write chunk at the correct offset in the temp file
            using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                fs.Seek(request.Offset, SeekOrigin.Begin);
                await fs.WriteAsync(chunkBytes, 0, chunkBytes.Length, cancellationToken);
            }

            _buffer.AddBytesWritten(request.UploadId, chunkBytes.Length);
            session.BytesUploaded = _buffer.GetBytesWritten(request.UploadId);
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
            Logger.LogError(ex, "[GCP] Chunk upload failed for session {UploadId}", request.UploadId);
            return StorageResult<ChunkUploadResult>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    protected override IResumableSessionStore GetSessionStore() => _sessionStore;

    /// <summary>
    /// Completes the resumable upload by streaming the buffered temp file to GCS atomically.
    /// Note: GCP uses temp file buffering — the actual GCS upload happens here, not during chunk uploads.
    /// The buffer is not durable across process restarts; if the process restarted after chunks were
    /// uploaded, this method will fail because the temp file will no longer exist.
    /// </summary>
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

            if (!_buffer.TryGetTempPath(uploadId, out var tempPath))
            {
                StorageTelemetry.RecordError(ProviderName, "resumable.complete");
                activity?.SetStatus(ActivityStatusCode.Error, "Buffer not found");
                return StorageResult<UploadResult>.Failure("Buffer for upload session not found. Was the process restarted?", StorageErrorCode.ProviderError);
            }

            var bucket = session.ProviderData["gcpBucket"];
            var path = session.ProviderData["gcpPath"];

            var uploadObject = new Object
            {
                Bucket = bucket,
                Name = path,
                ContentType = session.ContentType
            };
            if (session.Metadata is not null)
                uploadObject.Metadata = session.Metadata.ToDictionary(k => k.Key, v => v.Value);

            using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var result = await _storageClient.UploadObjectAsync(uploadObject, fs, null, cancellationToken);
            }

            _buffer.RemoveSession(uploadId);
            session.IsComplete = true;
            await _sessionStore.DeleteAsync(uploadId, cancellationToken);

            Logger.LogInformation("[GCP] Completed resumable upload session {UploadId} for {Path}", uploadId, path);
            StorageTelemetry.RecordResumableCompleted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult<UploadResult>.Success(new UploadResult
            {
                Path = path,
                SizeBytes = session.BytesUploaded
            });
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.complete");
            Logger.LogError(ex, "[GCP] CompleteResumableUpload failed for session {UploadId}", uploadId);
            return StorageResult<UploadResult>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    /// <summary>
    /// Aborts the resumable upload session and deletes the local temp file buffer.
    /// Note: GCP uses temp file buffering — no GCS multipart upload needs to be cancelled.
    /// </summary>
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

            _buffer.RemoveSession(uploadId);
            await _sessionStore.DeleteAsync(uploadId, cancellationToken);
            Logger.LogInformation("[GCP] Aborted resumable upload session {UploadId}", uploadId);
            StorageTelemetry.RecordResumableAborted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult.Success();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.abort");
            Logger.LogError(ex, "[GCP] AbortResumableUpload failed for session {UploadId}", uploadId);
            return StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    // ─── IPresignedUrlProvider ────────────────────────────────────────────────
    // Requires service account credentials (CredentialsPath or CredentialsJson).
    // Application Default Credentials do not support URL signing.

    public Task<StorageResult<string>> GetPresignedUploadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        if (_urlSigner is null)
            return Task.FromResult(StorageResult<string>.Failure(
                "Presigned upload URLs require service account credentials (set CredentialsPath or CredentialsJson).",
                StorageErrorCode.NotSupported));

        var url = _urlSigner.Sign(_options.Bucket, path, expiration, HttpMethod.Put);
        return Task.FromResult(StorageResult<string>.Success(url));
    }

    public Task<StorageResult<string>> GetPresignedDownloadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        if (_urlSigner is null)
            return Task.FromResult(StorageResult<string>.Failure(
                "Presigned download URLs require service account credentials (set CredentialsPath or CredentialsJson).",
                StorageErrorCode.NotSupported));

        var url = _urlSigner.Sign(_options.Bucket, path, expiration, HttpMethod.Get);
        return Task.FromResult(StorageResult<string>.Success(url));
    }

    protected override async Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesCoreAsync(
        string? prefix, ListOptions? options, CancellationToken cancellationToken)
    {
        var entries = new List<FileEntry>();

        var listOptions = new ListObjectsOptions
        {
            PageSize = options?.MaxResults
        };

        await foreach (var obj in _storageClient.ListObjectsAsync(_options.Bucket, prefix, listOptions).WithCancellation(cancellationToken))
        {
            entries.Add(new FileEntry
            {
                Path = obj.Name,
                SizeBytes = (long)(obj.Size ?? 0),
                ContentType = obj.ContentType,
                LastModified = obj.UpdatedDateTimeOffset,
                ETag = obj.ETag
            });

            if (options?.MaxResults.HasValue == true && entries.Count >= options.MaxResults)
                break;
        }

        return StorageResult<IReadOnlyList<FileEntry>>.Success(entries.AsReadOnly());
    }
}
