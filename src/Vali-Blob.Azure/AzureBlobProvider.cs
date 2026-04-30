using System.Net.Http;
using Azure;
using Azure.Core;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Providers;
using ValiBlob.Core.Resumable;

namespace ValiBlob.Azure;

/// <summary>
/// Azure Blob Storage provider implementation supporting uploads, downloads, presigned URLs, and resumable uploads.
/// </summary>
public sealed class AzureBlobProvider : BaseStorageProvider, IPresignedUrlProvider, IResumableUploadProvider
{
    private readonly BlobServiceClient _serviceClient;
    private readonly AzureBlobOptions _options;
    private readonly IResumableSessionStore _sessionStore;
    private readonly AzureResumableHandler _resumableHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureBlobProvider"/> class.
    /// </summary>
    /// <param name="serviceClient">The Azure Blob Storage service client.</param>
    /// <param name="options">Azure Blob Storage configuration options.</param>
    /// <param name="logger">Logger instance for diagnostic messages.</param>
    /// <param name="resilienceOptions">Resilience and retry configuration.</param>
    /// <param name="encryptionOptions">Encryption configuration for sensitive data.</param>
    /// <param name="pipeline">Storage pipeline for processing downloads and uploads.</param>
    /// <param name="sessionStore">Session store for resumable uploads.</param>
    /// <param name="resumableOptions">Configuration for resumable upload behavior.</param>
    /// <param name="httpClientFactory">Factory function for creating HTTP clients.</param>
    public AzureBlobProvider(
        BlobServiceClient serviceClient,
        IOptions<AzureBlobOptions> options,
        ILogger<AzureBlobProvider> logger,
        IOptions<ResilienceOptions> resilienceOptions,
        IOptions<EncryptionOptions> encryptionOptions,
        StoragePipelineBuilder pipeline,
        IResumableSessionStore sessionStore,
        IOptions<ResumableUploadOptions> resumableOptions,
        Func<string, HttpClient> httpClientFactory)
        : base(logger, resilienceOptions, encryptionOptions, pipeline, httpClientFactory)
    {
        _serviceClient = serviceClient;
        _options = options.Value;
        _sessionStore = sessionStore;
        _resumableHandler = new AzureResumableHandler(
            serviceClient,
            options.Value,
            sessionStore,
            resumableOptions.Value,
            logger);
    }

    /// <summary>
    /// Gets the name of this storage provider.
    /// </summary>
    public override string ProviderName => nameof(StorageProviderType.Azure);

    private BlobContainerClient GetContainer(string? containerOverride = null) =>
        _serviceClient.GetBlobContainerClient(ResolveBucket(containerOverride, _options.Container));

    // ─── Core storage operations ─────────────────────────────────────────────

    protected override async Task<StorageResult<UploadResult>> UploadCoreAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var container = GetContainer(request.BucketOverride);
        if (_options.CreateContainerIfNotExists)
            await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var blob = container.GetBlobClient(request.Path);

        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = request.ContentType },
            Metadata = request.Metadata?.ToDictionary(k => k.Key, v => v.Value),
            TransferOptions = new StorageTransferOptions
            {
                MaximumTransferSize = _options.MultipartChunkSizeMb * 1024 * 1024
            }
        };

        if (progress is not null)
        {
            uploadOptions.ProgressHandler = new Progress<long>(bytes =>
                progress.Report(new UploadProgress(bytes, request.ContentLength)));
        }

        var response = await blob.UploadAsync(request.Content, uploadOptions, cancellationToken);

        return StorageResult<UploadResult>.Success(new UploadResult
        {
            Path = request.Path,
            ETag = response.Value.ETag.ToString(),
            SizeBytes = request.ContentLength ?? 0
        });
    }

    protected override async Task<StorageResult<Stream>> DownloadCoreAsync(
        DownloadRequest request,
        CancellationToken cancellationToken)
    {
        var blob = GetContainer(request.BucketOverride).GetBlobClient(request.Path);
        var ms = new MemoryStream();

        if (request.Range is not null)
        {
            var rangeLength = request.Range.To.HasValue ? request.Range.To.Value - request.Range.From : (long?)null;
            var downloadOptions = new BlobDownloadOptions
            {
                Range = new HttpRange(request.Range.From, rangeLength)
            };
            var streamResponse = await blob.DownloadStreamingAsync(downloadOptions, cancellationToken);
            await streamResponse.Value.Content.CopyToAsync(ms);
        }
        else
        {
            await blob.DownloadToAsync(ms, cancellationToken);
        }

        ms.Position = 0;
        return StorageResult<Stream>.Success(ms);
    }

    protected override async Task<StorageResult> DeleteCoreAsync(string path, CancellationToken cancellationToken)
    {
        await GetContainer().GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        return StorageResult.Success();
    }

    protected override async Task<StorageResult<bool>> ExistsCoreAsync(string path, CancellationToken cancellationToken)
    {
        var exists = await GetContainer().GetBlobClient(path).ExistsAsync(cancellationToken);
        return StorageResult<bool>.Success(exists.Value);
    }

    protected override Task<StorageResult<string>> GetUrlCoreAsync(string path, CancellationToken cancellationToken)
    {
        var url = _options.CdnBaseUrl is not null
            ? $"{_options.CdnBaseUrl.TrimEnd('/')}/{path}"
            : GetContainer().GetBlobClient(path).Uri.ToString();

        return Task.FromResult(StorageResult<string>.Success(url));
    }

    protected override async Task<StorageResult> CopyCoreAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        var sourceBlob = GetContainer().GetBlobClient(sourcePath);
        var destBlob = GetContainer().GetBlobClient(destinationPath);
        var copyOperation = await destBlob.StartCopyFromUriAsync(sourceBlob.Uri, cancellationToken: cancellationToken);
        await copyOperation.WaitForCompletionAsync(cancellationToken);
        return StorageResult.Success();
    }

    protected override async Task<StorageResult<FileMetadata>> GetMetadataCoreAsync(string path, CancellationToken cancellationToken)
    {
        var properties = await GetContainer().GetBlobClient(path).GetPropertiesAsync(cancellationToken: cancellationToken);

        return StorageResult<FileMetadata>.Success(new FileMetadata
        {
            Path = path,
            SizeBytes = properties.Value.ContentLength,
            ContentType = properties.Value.ContentType,
            LastModified = properties.Value.LastModified,
            ETag = properties.Value.ETag.ToString(),
            CustomMetadata = properties.Value.Metadata
        });
    }

    protected override async Task<StorageResult> SetMetadataCoreAsync(string path, IDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        await GetContainer().GetBlobClient(path).SetMetadataAsync(metadata, cancellationToken: cancellationToken);
        return StorageResult.Success();
    }

    protected override async Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesCoreAsync(
        string? prefix, ListOptions? options, CancellationToken cancellationToken)
    {
        var entries = new List<FileEntry>();
        var pages = GetContainer()
            .GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken)
            .AsPages(options?.ContinuationToken, options?.MaxResults);

        await foreach (var page in pages.WithCancellation(cancellationToken))
        {
            foreach (var blob in page.Values)
            {
                entries.Add(new FileEntry
                {
                    Path = blob.Name,
                    SizeBytes = blob.Properties.ContentLength ?? 0,
                    ContentType = blob.Properties.ContentType,
                    LastModified = blob.Properties.LastModified,
                    ETag = blob.Properties.ETag?.ToString()
                });
            }

            if (options?.MaxResults.HasValue == true && entries.Count >= options.MaxResults)
                break;
        }

        return StorageResult<IReadOnlyList<FileEntry>>.Success(entries.AsReadOnly());
    }

    // ─── Batch delete (Azure-optimized with concurrency) ────────────────────

    /// <summary>
    /// Deletes multiple blobs concurrently with a semaphore-controlled batch operation.
    /// </summary>
    /// <param name="paths">Paths of the blobs to delete.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Result containing deletion statistics and any errors encountered.</returns>
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

        var errors = new System.Collections.Concurrent.ConcurrentBag<BatchDeleteError>();
        var deleted = 0;
        var container = GetContainer();

        using var semaphore = new SemaphoreSlim(32);
        var tasks = pathList.Select(async path =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await container.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: cancellationToken);
                Interlocked.Increment(ref deleted);
            }
            catch (Exception ex)
            {
                errors.Add(new BatchDeleteError { Path = path, Reason = ex.Message });
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        var errorList = errors.ToList();
        return StorageResult<BatchDeleteResult>.Success(new BatchDeleteResult
        {
            TotalRequested = pathList.Count,
            Deleted = deleted,
            Failed = errorList.Count,
            Errors = errorList.AsReadOnly()
        });
    }

    // ─── IResumableUploadProvider — delegates to AzureResumableHandler ───────

    protected override IResumableSessionStore GetSessionStore() => _sessionStore;

    /// <summary>
    /// Initiates a resumable upload session.
    /// </summary>
    /// <param name="request">Upload request parameters including file metadata.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A resumable upload session with upload ID and configuration.</returns>
    public Task<StorageResult<ResumableUploadSession>> StartResumableUploadAsync(
        ResumableUploadRequest request,
        CancellationToken cancellationToken = default)
        => _resumableHandler.StartAsync(request, ProviderName, ResolveBucket, cancellationToken);

    /// <summary>
    /// Uploads a single chunk of a resumable upload.
    /// </summary>
    /// <param name="request">Chunk upload request with upload ID and chunk data.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Upload result for the chunk including position and status.</returns>
    public Task<StorageResult<ChunkUploadResult>> UploadChunkAsync(
        ResumableChunkRequest request,
        CancellationToken cancellationToken = default)
        => _resumableHandler.UploadChunkAsync(request, ProviderName, cancellationToken);

    /// <summary>
    /// Completes a resumable upload and finalizes the blob.
    /// </summary>
    /// <param name="uploadId">The upload session identifier.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Final upload result with ETag and file metadata.</returns>
    public Task<StorageResult<UploadResult>> CompleteResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
        => _resumableHandler.CompleteAsync(uploadId, ProviderName, cancellationToken);

    /// <summary>
    /// Aborts an active resumable upload and cleans up temporary resources.
    /// </summary>
    /// <param name="uploadId">The upload session identifier to abort.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Result indicating success or failure of the abort operation.</returns>
    public Task<StorageResult> AbortResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
        => _resumableHandler.AbortAsync(uploadId, ProviderName, cancellationToken);

    // ─── IPresignedUrlProvider ───────────────────────────────────────────────

    /// <summary>
    /// Generates a presigned SAS URL for uploading a blob.
    /// </summary>
    /// <param name="path">The blob path.</param>
    /// <param name="expiration">Time span after which the URL expires.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A presigned URL with write and create permissions.</returns>
    public Task<StorageResult<string>> GetPresignedUploadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        var blobClient = GetContainer().GetBlobClient(path);
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _options.Container,
            BlobName = path,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(expiration)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);

        var uri = blobClient.GenerateSasUri(sasBuilder);
        return Task.FromResult(StorageResult<string>.Success(uri.ToString()));
    }

    /// <summary>
    /// Generates a presigned SAS URL for downloading a blob.
    /// </summary>
    /// <param name="path">The blob path.</param>
    /// <param name="expiration">Time span after which the URL expires.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A presigned URL with read-only permissions.</returns>
    public Task<StorageResult<string>> GetPresignedDownloadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        var blobClient = GetContainer().GetBlobClient(path);
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _options.Container,
            BlobName = path,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(expiration)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var uri = blobClient.GenerateSasUri(sasBuilder);
        return Task.FromResult(StorageResult<string>.Success(uri.ToString()));
    }
}
