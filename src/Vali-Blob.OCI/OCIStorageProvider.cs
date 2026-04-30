using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oci.ObjectstorageService;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Providers;
using ValiBlob.Core.Resumable;

namespace ValiBlob.OCI;

/// <summary>
/// Oracle Cloud Infrastructure (OCI) Object Storage provider implementation with resumable upload and presigned URL support.
/// </summary>
public sealed class OCIStorageProvider : BaseStorageProvider, IResumableUploadProvider, IPresignedUrlProvider
{
    private readonly ObjectStorageClient _client;
    private readonly OCIStorageOptions _options;
    private readonly IResumableSessionStore _sessionStore;
    private readonly OciResumableUploadHandler _resumableHandler;
    private readonly OciPresignedUrlHandler _presignedHandler;

    public OCIStorageProvider(
        ObjectStorageClient client,
        IOptions<OCIStorageOptions> options,
        ILogger<OCIStorageProvider> logger,
        IOptions<ResilienceOptions> resilienceOptions,
        IOptions<EncryptionOptions> encryptionOptions,
        StoragePipelineBuilder pipeline,
        IResumableSessionStore sessionStore,
        IOptions<ResumableUploadOptions> resumableOptions,
        Func<string, HttpClient> httpClientFactory)
        : base(logger, resilienceOptions, encryptionOptions, pipeline, httpClientFactory)
    {
        _client = client;
        _options = options.Value;
        _sessionStore = sessionStore;

        _resumableHandler = new OciResumableUploadHandler(
            client, _options, sessionStore, resumableOptions.Value, logger);

        _presignedHandler = new OciPresignedUrlHandler(client, _options, logger);
    }

    /// <summary>
    /// Gets the provider name identifier.
    /// </summary>
    public override string ProviderName => nameof(StorageProviderType.OCI);

    private string GetServiceBaseUrl() =>
        _options.ServiceUrl ?? $"https://objectstorage.{_options.Region}.oraclecloud.com";

    // ─── Core overrides ───────────────────────────────────────────────────────

    /// <summary>
    /// Uploads content to OCI Object Storage.
    /// </summary>
    protected override async Task<StorageResult<UploadResult>> UploadCoreAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var bucket = ResolveBucket(request.BucketOverride, _options.Bucket);
        var response = await _client.PutObject(OciObjectRequestBuilder.BuildPut(_options.Namespace, bucket, request));

        return StorageResult<UploadResult>.Success(new UploadResult
        {
            Path = request.Path,
            ETag = response.ETag,
            SizeBytes = request.ContentLength ?? 0
        });
    }

    /// <summary>
    /// Downloads content from OCI Object Storage.
    /// </summary>
    protected override async Task<StorageResult<Stream>> DownloadCoreAsync(
        DownloadRequest request, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucket(request.BucketOverride, _options.Bucket);
        var response = await _client.GetObject(OciObjectRequestBuilder.BuildGet(_options.Namespace, bucket, request));
        return StorageResult<Stream>.Success(response.InputStream);
    }

    /// <summary>
    /// Deletes an object from OCI Object Storage.
    /// </summary>
    protected override async Task<StorageResult> DeleteCoreAsync(string path, CancellationToken cancellationToken)
    {
        await _client.DeleteObject(OciObjectRequestBuilder.BuildDelete(_options.Namespace, _options.Bucket, path));
        return StorageResult.Success();
    }

    /// <summary>
    /// Checks if an object exists in OCI Object Storage.
    /// </summary>
    protected override async Task<StorageResult<bool>> ExistsCoreAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await _client.HeadObject(OciObjectRequestBuilder.BuildHead(_options.Namespace, _options.Bucket, path));
            return StorageResult<bool>.Success(true);
        }
        catch (Oci.Common.Model.OciException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return StorageResult<bool>.Success(false);
        }
    }

    /// <summary>
    /// Constructs the public URL for an object in OCI Object Storage.
    /// </summary>
    protected override Task<StorageResult<string>> GetUrlCoreAsync(string path, CancellationToken cancellationToken)
    {
        var url = _options.CdnBaseUrl is not null
            ? $"{_options.CdnBaseUrl.TrimEnd('/')}/{path}"
            : $"{GetServiceBaseUrl()}/n/{_options.Namespace}/b/{_options.Bucket}/o/{path}";

        return Task.FromResult(StorageResult<string>.Success(url));
    }

    /// <summary>
    /// Copies an object within OCI Object Storage.
    /// </summary>
    protected override async Task<StorageResult> CopyCoreAsync(
        string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await _client.CopyObject(OciObjectRequestBuilder.BuildCopy(
            _options.Namespace, _options.Bucket, _options.Region, sourcePath, destinationPath));
        return StorageResult.Success();
    }

    /// <summary>
    /// Retrieves metadata for an object in OCI Object Storage.
    /// </summary>
    protected override async Task<StorageResult<FileMetadata>> GetMetadataCoreAsync(
        string path, CancellationToken cancellationToken)
    {
        var response = await _client.HeadObject(
            OciObjectRequestBuilder.BuildHead(_options.Namespace, _options.Bucket, path));

        return StorageResult<FileMetadata>.Success(new FileMetadata
        {
            Path = path,
            SizeBytes = response.ContentLength ?? 0,
            ContentType = response.ContentType,
            LastModified = response.LastModified,
            ETag = response.ETag
        });
    }

    /// <summary>
    /// Updates metadata for an object in OCI Object Storage (not supported; requires re-upload).
    /// </summary>
    protected override Task<StorageResult> SetMetadataCoreAsync(
        string path, IDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        Logger.LogWarning("[OCI] SetMetadata requires re-upload in OCI Object Storage.");
        return Task.FromResult(StorageResult.Failure("OCI requires re-upload to update metadata.", StorageErrorCode.NotSupported));
    }

    /// <summary>
    /// Lists objects in OCI Object Storage with optional prefix filtering and pagination.
    /// </summary>
    protected override async Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesCoreAsync(
        string? prefix, ListOptions? options, CancellationToken cancellationToken)
    {
        var response = await _client.ListObjects(
            OciObjectRequestBuilder.BuildList(_options.Namespace, _options.Bucket, prefix, options?.MaxResults ?? 1000));

        var entries = response.ListObjects.Objects.Select(o => new FileEntry
        {
            Path = o.Name,
            SizeBytes = o.Size ?? 0,
            LastModified = o.TimeModified,
            ETag = o.Etag
        }).ToList();

        return StorageResult<IReadOnlyList<FileEntry>>.Success(entries.AsReadOnly());
    }

    // ─── IResumableUploadProvider ─────────────────────────────────────────────

    /// <summary>
    /// Starts a resumable upload session in OCI Object Storage.
    /// </summary>
    public Task<StorageResult<ResumableUploadSession>> StartResumableUploadAsync(
        ResumableUploadRequest request, CancellationToken cancellationToken = default) =>
        _resumableHandler.StartAsync(request, cancellationToken);

    /// <summary>
    /// Uploads a chunk to a resumable upload session in OCI Object Storage.
    /// </summary>
    public Task<StorageResult<ChunkUploadResult>> UploadChunkAsync(
        ResumableChunkRequest request, CancellationToken cancellationToken = default) =>
        _resumableHandler.UploadChunkAsync(request, cancellationToken);

    /// <summary>
    /// Completes a resumable upload session in OCI Object Storage.
    /// </summary>
    public Task<StorageResult<UploadResult>> CompleteResumableUploadAsync(
        string uploadId, CancellationToken cancellationToken = default) =>
        _resumableHandler.CompleteAsync(uploadId, cancellationToken);

    /// <summary>
    /// Aborts a resumable upload session in OCI Object Storage.
    /// </summary>
    public Task<StorageResult> AbortResumableUploadAsync(
        string uploadId, CancellationToken cancellationToken = default) =>
        _resumableHandler.AbortAsync(uploadId, cancellationToken);

    protected override IResumableSessionStore GetSessionStore() => _sessionStore;

    // ─── IPresignedUrlProvider ────────────────────────────────────────────────

    /// <summary>
    /// Generates a presigned URL for uploading an object to OCI Object Storage.
    /// </summary>
    public Task<StorageResult<string>> GetPresignedUploadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default) =>
        _presignedHandler.GetPresignedUploadUrlAsync(path, expiration, GetServiceBaseUrl(), cancellationToken);

    /// <summary>
    /// Generates a presigned URL for downloading an object from OCI Object Storage.
    /// </summary>
    public Task<StorageResult<string>> GetPresignedDownloadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default) =>
        _presignedHandler.GetPresignedDownloadUrlAsync(path, expiration, GetServiceBaseUrl(), cancellationToken);
}
