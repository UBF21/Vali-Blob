using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Providers;
using ValiBlob.Core.Resumable;
using ValiBlob.Core.Telemetry;

namespace ValiBlob.Azure;

public sealed class AzureBlobProvider : BaseStorageProvider, IPresignedUrlProvider, IResumableUploadProvider
{
    private readonly BlobServiceClient _serviceClient;
    private readonly AzureBlobOptions _options;
    private readonly IResumableSessionStore _sessionStore;
    private readonly ResumableUploadOptions _resumableOptions;

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
        _resumableOptions = resumableOptions.Value;
    }

    public override string ProviderName => "Azure";

    private BlobContainerClient GetContainer(string? containerOverride = null) =>
        _serviceClient.GetBlobContainerClient(ResolveBucket(containerOverride, _options.Container));

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

    // ─── IResumableUploadProvider ───────────────────────────────────────────

    public async Task<StorageResult<ResumableUploadSession>> StartResumableUploadAsync(
        ResumableUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.start", ProviderName, request.Path);
        try
        {
            var container = GetContainer(request.BucketOverride);
            if (_options.CreateContainerIfNotExists)
                await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

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
                    ["azureContainer"] = ResolveBucket(request.BucketOverride, _options.Container),
                    ["azureBlobName"] = request.Path,
                    ["azureBlockIds"] = string.Empty  // comma-separated base64 block IDs in order
                }
            };

            await _sessionStore.SaveAsync(session, cancellationToken);
            Logger.LogInformation("[Azure] Started resumable upload session {SessionId} for {Path}", session.UploadId, session.Path);
            StorageTelemetry.RecordResumableStarted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult<ResumableUploadSession>.Success(session);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.start");
            Logger.LogError(ex, "[Azure] Failed to start resumable upload for {Path}", request.Path);
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

            // Block ID: deterministic base64 of the offset (padded to 16 chars for consistent length)
            var blockIdStr = request.Offset.ToString("D20");
            var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(blockIdStr));

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

            var blockBlobClient = _serviceClient
                .GetBlobContainerClient(session.ProviderData["azureContainer"])
                .GetBlockBlobClient(session.ProviderData["azureBlobName"]);

            using var chunkStream = new MemoryStream(chunkBytes);

            // Pass MD5 bytes to Azure for server-side integrity validation
            byte[]? md5Bytes = _resumableOptions.EnableChecksumValidation
                ? Convert.FromBase64String(chunkMd5)
                : null;
            await blockBlobClient.StageBlockAsync(blockId, chunkStream, md5Bytes, null, null, cancellationToken);

            // Append block ID to ordered list
            var existing = session.ProviderData["azureBlockIds"];
            session.ProviderData["azureBlockIds"] = string.IsNullOrEmpty(existing)
                ? blockId
                : $"{existing},{blockId}";
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
            Logger.LogError(ex, "[Azure] Chunk upload failed for session {UploadId}", request.UploadId);
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

            var blockIds = session.ProviderData["azureBlockIds"].Split(',');
            var blockBlobClient = _serviceClient
                .GetBlobContainerClient(session.ProviderData["azureContainer"])
                .GetBlockBlobClient(session.ProviderData["azureBlobName"]);

            var commitOptions = new CommitBlockListOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = session.ContentType },
                Metadata = session.Metadata?.ToDictionary(k => k.Key, v => v.Value)
            };

            var response = await blockBlobClient.CommitBlockListAsync(blockIds, commitOptions, cancellationToken);

            session.IsComplete = true;
            await _sessionStore.DeleteAsync(uploadId, cancellationToken);

            Logger.LogInformation("[Azure] Completed resumable upload session {UploadId} for {Path}", uploadId, session.Path);
            StorageTelemetry.RecordResumableCompleted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult<UploadResult>.Success(new UploadResult
            {
                Path = session.Path,
                ETag = response.Value.ETag.ToString(),
                SizeBytes = session.BytesUploaded
            });
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.complete");
            Logger.LogError(ex, "[Azure] CompleteResumableUpload failed for session {UploadId}", uploadId);
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

            // Azure staged blocks expire automatically after 7 days — simply discard the session.
            // Optionally delete the blob if it was partially committed.
            await _sessionStore.DeleteAsync(uploadId, cancellationToken);
            Logger.LogInformation("[Azure] Aborted resumable upload session {UploadId}", uploadId);
            StorageTelemetry.RecordResumableAborted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult.Success();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.abort");
            Logger.LogError(ex, "[Azure] AbortResumableUpload failed for session {UploadId}", uploadId);
            return StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    // ─── IPresignedUrlProvider ───────────────────────────────────────────────

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
