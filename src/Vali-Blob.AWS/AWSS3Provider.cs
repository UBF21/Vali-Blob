using System.Collections.Generic;
using System.Net.Http;
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

namespace ValiBlob.AWS;

/// <summary>
/// AWS S3 storage provider implementation supporting standard, multipart, resumable uploads and presigned URLs.
/// </summary>
public sealed class AWSS3Provider : BaseStorageProvider, IPresignedUrlProvider, IResumableUploadProvider
{
    private readonly IAmazonS3 _s3Client;
    private readonly AWSS3Options _options;
    private readonly IResumableSessionStore _sessionStore;
    private readonly S3ResumableHandler _resumableHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="AWSS3Provider"/> class.
    /// </summary>
    /// <param name="s3Client">The AWS S3 client.</param>
    /// <param name="options">S3 configuration options.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="resilienceOptions">Resilience policy configuration.</param>
    /// <param name="encryptionOptions">Encryption configuration.</param>
    /// <param name="pipeline">Storage operation pipeline.</param>
    /// <param name="sessionStore">Resumable upload session storage.</param>
    /// <param name="resumableOptions">Resumable upload configuration.</param>
    /// <param name="httpClientFactory">Factory for creating HTTP clients.</param>
    public AWSS3Provider(
        IAmazonS3 s3Client,
        IOptions<AWSS3Options> options,
        ILogger<AWSS3Provider> logger,
        IOptions<ResilienceOptions> resilienceOptions,
        IOptions<EncryptionOptions> encryptionOptions,
        StoragePipelineBuilder pipeline,
        IResumableSessionStore sessionStore,
        IOptions<ResumableUploadOptions> resumableOptions,
        Func<string, HttpClient> httpClientFactory)
        : base(logger, resilienceOptions, encryptionOptions, pipeline, httpClientFactory)
    {
        _s3Client = s3Client;
        _options = options.Value;
        _sessionStore = sessionStore;
        _resumableHandler = new S3ResumableHandler(
            s3Client,
            options.Value,
            sessionStore,
            resumableOptions.Value,
            logger);
    }

    /// <summary>
    /// Gets the provider name.
    /// </summary>
    public override string ProviderName => nameof(StorageProviderType.AWS);

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

    /// <summary>
    /// Deletes multiple files in batches, up to 1000 objects per request.
    /// </summary>
    /// <param name="paths">Paths to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with deletion summary.</returns>
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

    /// <summary>
    /// Gets the resumable upload session store.
    /// </summary>
    /// <returns>The session store instance.</returns>
    protected override IResumableSessionStore GetSessionStore() => _sessionStore;

    /// <summary>
    /// Initiates a resumable upload session.
    /// </summary>
    /// <param name="request">Resumable upload request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with resumable upload session details.</returns>
    public Task<StorageResult<ResumableUploadSession>> StartResumableUploadAsync(
        ResumableUploadRequest request,
        CancellationToken cancellationToken = default)
        => _resumableHandler.StartAsync(request, ProviderName, ResolveBucket, cancellationToken);

    /// <summary>
    /// Uploads a chunk of data for a resumable upload.
    /// </summary>
    /// <param name="request">Chunk upload request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with chunk upload details.</returns>
    public Task<StorageResult<ChunkUploadResult>> UploadChunkAsync(
        ResumableChunkRequest request,
        CancellationToken cancellationToken = default)
        => _resumableHandler.UploadChunkAsync(request, ProviderName, cancellationToken);

    /// <summary>
    /// Completes a resumable upload.
    /// </summary>
    /// <param name="uploadId">The upload session ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with final upload details.</returns>
    public Task<StorageResult<UploadResult>> CompleteResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
        => _resumableHandler.CompleteAsync(uploadId, ProviderName, cancellationToken);

    /// <summary>
    /// Aborts a resumable upload and releases all uploaded chunks.
    /// </summary>
    /// <param name="uploadId">The upload session ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the abort operation.</returns>
    public Task<StorageResult> AbortResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
        => _resumableHandler.AbortAsync(uploadId, ProviderName, cancellationToken);

    // ─── IPresignedUrlProvider ───────────────────────────────────────────────

    /// <summary>
    /// Generates a presigned URL for uploading a file.
    /// </summary>
    /// <param name="path">The file path in S3.</param>
    /// <param name="expiration">URL expiration duration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with the presigned upload URL.</returns>
    public Task<StorageResult<string>> GetPresignedUploadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default)
        => S3PresignedUrlHelper.GetPresignedUploadUrlAsync(_s3Client, _options.Bucket, path, expiration);

    /// <summary>
    /// Generates a presigned URL for downloading a file.
    /// </summary>
    /// <param name="path">The file path in S3.</param>
    /// <param name="expiration">URL expiration duration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with the presigned download URL.</returns>
    public Task<StorageResult<string>> GetPresignedDownloadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default)
        => S3PresignedUrlHelper.GetPresignedDownloadUrlAsync(_s3Client, _options.Bucket, path, expiration);
}
