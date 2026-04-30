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

/// <summary>
/// Google Cloud Storage provider implementation with resumable upload and presigned URL support.
/// </summary>
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

    /// <summary>
    /// Gets the provider name identifier.
    /// </summary>
    public override string ProviderName => nameof(StorageProviderType.GCP);

    /// <summary>
    /// Uploads content to GCP using the Google Cloud Storage API.
    /// </summary>
    protected override async Task<StorageResult<UploadResult>> UploadCoreAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var bucket = ResolveBucket(request.BucketOverride, _options.Bucket);
        var uploadObject = GcpObjectHelper.BuildUploadObject(bucket, request.Path, request.ContentType, request.Metadata);

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

    /// <summary>
    /// Downloads content from GCP, optionally applying range constraints.
    /// </summary>
    protected override async Task<StorageResult<Stream>> DownloadCoreAsync(
        DownloadRequest request, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucket(request.BucketOverride, _options.Bucket);
        var ms = new MemoryStream();
        await _storageClient.DownloadObjectAsync(bucket, request.Path, ms, cancellationToken: cancellationToken);

        if (request.Range is not null)
            return StorageResult<Stream>.Success(await GcpObjectHelper.ApplyRangeAsync(ms, request.Range, cancellationToken));

        ms.Position = 0;
        return StorageResult<Stream>.Success(ms);
    }

    /// <summary>
    /// Deletes an object from GCP.
    /// </summary>
    protected override async Task<StorageResult> DeleteCoreAsync(string path, CancellationToken cancellationToken)
    {
        await _storageClient.DeleteObjectAsync(_options.Bucket, path, cancellationToken: cancellationToken);
        return StorageResult.Success();
    }

    /// <summary>
    /// Checks if an object exists in GCP.
    /// </summary>
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

    /// <summary>
    /// Constructs the public URL for an object in GCP.
    /// </summary>
    protected override Task<StorageResult<string>> GetUrlCoreAsync(string path, CancellationToken cancellationToken)
    {
        var url = GcpObjectHelper.ResolvePublicUrl(_options.Bucket, _options.CdnBaseUrl, path);

        return Task.FromResult(StorageResult<string>.Success(url));
    }

    /// <summary>
    /// Copies an object within GCP.
    /// </summary>
    protected override async Task<StorageResult> CopyCoreAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await _storageClient.CopyObjectAsync(_options.Bucket, sourcePath, _options.Bucket, destinationPath, cancellationToken: cancellationToken);
        return StorageResult.Success();
    }

    /// <summary>
    /// Retrieves metadata for an object in GCP.
    /// </summary>
    protected override async Task<StorageResult<FileMetadata>> GetMetadataCoreAsync(string path, CancellationToken cancellationToken)
    {
        var obj = await _storageClient.GetObjectAsync(_options.Bucket, path, cancellationToken: cancellationToken);
        return StorageResult<FileMetadata>.Success(GcpObjectHelper.ToFileMetadata(path, obj));
    }

    /// <summary>
    /// Updates metadata for an object in GCP.
    /// </summary>
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
            if (request.TotalSize > 0)
            {
                using var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Write, FileShare.None);
                fs.SetLength(request.TotalSize);
            }

            var session = new ResumableUploadSession
            {
                UploadId = uploadId,
                Path = request.Path,
                BucketOverride = request.BucketOverride,
                TotalSize = request.TotalSize,
                ContentType = request.ContentType,
                Metadata = request.Metadata,
                ExpiresAt = DateTimeOffset.UtcNow.Add(request.Options?.SessionExpiration ?? _resumableOptions.SessionExpiration),
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
            var validation = await ValidateSessionAndBuffer<ChunkUploadResult>(request.UploadId, "resumable.chunk", activity, cancellationToken);
            if (validation.failed is not null) return validation.failed.Value;

            var (session, tempPath) = (validation.session!, validation.tempPath!);

            var chunkBytes = await StreamReadHelper.ReadChunkAsync(request.Data, request.Length, cancellationToken)
                .ConfigureAwait(false);

            if (_resumableOptions.EnableChecksumValidation || request.ExpectedMd5 is not null)
            {
                var checksumError = ValidateChunkChecksum(chunkBytes, request.ExpectedMd5);
                if (checksumError is not null)
                {
                    StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
                    activity?.SetStatus(ActivityStatusCode.Error, checksumError);
                    return StorageResult<ChunkUploadResult>.Failure(checksumError, StorageErrorCode.ValidationFailed);
                }
            }

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

            return StorageResult<ChunkUploadResult>.Success(new ChunkUploadResult
            {
                UploadId = request.UploadId,
                BytesUploaded = session.BytesUploaded,
                TotalSize = session.TotalSize,
                IsReadyToComplete = session.BytesUploaded >= session.TotalSize
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
    /// The buffer is not durable across process restarts.
    /// </summary>
    public async Task<StorageResult<UploadResult>> CompleteResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.complete", ProviderName, uploadId);
        try
        {
            var validation = await ValidateSessionAndBuffer<UploadResult>(uploadId, "resumable.complete", activity, cancellationToken);
            if (validation.failed is not null) return validation.failed.Value;

            var (session, tempPath) = (validation.session!, validation.tempPath!);
            var bucket = session.ProviderData["gcpBucket"];
            var path = session.ProviderData["gcpPath"];
            var uploadObject = GcpObjectHelper.BuildUploadObject(bucket, path, session.ContentType, session.Metadata);

            using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                await _storageClient.UploadObjectAsync(uploadObject, fs, null, cancellationToken);

            _buffer.RemoveSession(uploadId);
            session.IsComplete = true;
            await _sessionStore.DeleteAsync(uploadId, cancellationToken);

            Logger.LogInformation("[GCP] Completed resumable upload session {UploadId} for {Path}", uploadId, path);
            StorageTelemetry.RecordResumableCompleted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult<UploadResult>.Success(new UploadResult { Path = path, SizeBytes = session.BytesUploaded });
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

    /// <summary>
    /// Generates a presigned URL for uploading an object to GCP.
    /// </summary>
    public Task<StorageResult<string>> GetPresignedUploadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default) =>
        GcpObjectHelper.PresignedUrl(_urlSigner, _options.Bucket, path, expiration, HttpMethod.Put);

    /// <summary>
    /// Generates a presigned URL for downloading an object from GCP.
    /// </summary>
    public Task<StorageResult<string>> GetPresignedDownloadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default) =>
        GcpObjectHelper.PresignedUrl(_urlSigner, _options.Bucket, path, expiration, HttpMethod.Get);

    /// <summary>
    /// Lists objects in GCP with optional prefix filtering and pagination.
    /// </summary>
    protected override async Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesCoreAsync(
        string? prefix, ListOptions? options, CancellationToken cancellationToken)
    {
        var entries = new List<FileEntry>();
        var listOptions = new ListObjectsOptions { PageSize = options?.MaxResults };

        await foreach (var obj in _storageClient.ListObjectsAsync(_options.Bucket, prefix, listOptions).WithCancellation(cancellationToken))
        {
            entries.Add(GcpObjectHelper.ToFileEntry(obj));
            if (options?.MaxResults.HasValue == true && entries.Count >= options.MaxResults)
                break;
        }

        return StorageResult<IReadOnlyList<FileEntry>>.Success(entries.AsReadOnly());
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private async Task<(StorageResult<T>? failed, ResumableUploadSession? session, string? tempPath)>
        ValidateSessionAndBuffer<T>(string uploadId, string operation, Activity? activity, CancellationToken ct)
    {
        var session = await _sessionStore.GetAsync(uploadId, ct);
        if (session is null)
        {
            StorageTelemetry.RecordError(ProviderName, operation);
            activity?.SetStatus(ActivityStatusCode.Error, "Session not found");
            return (StorageResult<T>.Failure($"Upload session '{uploadId}' not found or expired.", StorageErrorCode.FileNotFound), null, null);
        }
        if (session.IsAborted)
        {
            StorageTelemetry.RecordError(ProviderName, operation);
            activity?.SetStatus(ActivityStatusCode.Error, "Session aborted");
            return (StorageResult<T>.Failure("Upload session has been aborted.", StorageErrorCode.ValidationFailed), null, null);
        }
        if (!_buffer.TryGetTempPath(uploadId, out var tempPath))
        {
            StorageTelemetry.RecordError(ProviderName, operation);
            activity?.SetStatus(ActivityStatusCode.Error, "Buffer not found");
            return (StorageResult<T>.Failure("Buffer for upload session not found. Was the process restarted?", StorageErrorCode.ProviderError), null, null);
        }
        return (null, session, tempPath);
    }

    private static string? ValidateChunkChecksum(byte[] chunkBytes, string? expectedMd5)
    {
        if (expectedMd5 is null) return null;
        var actualMd5 = ChunkChecksumHelper.ComputeMd5Base64(chunkBytes);
        return ChunkChecksumHelper.Validate(actualMd5, expectedMd5);
    }
}
