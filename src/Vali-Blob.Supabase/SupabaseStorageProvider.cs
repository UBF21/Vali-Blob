using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Providers;

namespace ValiBlob.Supabase;

/// <summary>
/// Storage provider implementation for Supabase Storage, supporting standard operations, resumable uploads, and presigned URLs.
/// </summary>
public sealed class SupabaseStorageProvider : BaseStorageProvider, IPresignedUrlProvider, IResumableUploadProvider
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseStorageOptions _options;
    private readonly SupabaseTusHandler _tusHandler;

    /// <summary>
    /// Initializes a new instance of the Supabase storage provider.
    /// </summary>
    public SupabaseStorageProvider(
        HttpClient httpClient,
        IOptions<SupabaseStorageOptions> options,
        ILogger<SupabaseStorageProvider> logger,
        IOptions<ResilienceOptions> resilienceOptions,
        IOptions<EncryptionOptions> encryptionOptions,
        StoragePipelineBuilder pipeline,
        IResumableSessionStore sessionStore,
        IOptions<ResumableUploadOptions> resumableOptions,
        Func<string, HttpClient> httpClientFactory)
        : base(logger, resilienceOptions, encryptionOptions, pipeline, httpClientFactory)
    {
        _httpClient = httpClient;
        _options = options.Value;

        var tusBaseUrl = $"{_options.Url.TrimEnd('/')}/storage/v1/upload/resumable";
        _tusHandler = new SupabaseTusHandler(
            httpClient,
            sessionStore,
            resumableOptions.Value,
            logger,
            tusBaseUrl,
            ProviderName);
    }

    /// <summary>Gets the provider name.</summary>
    public override string ProviderName => nameof(StorageProviderType.Supabase);

    private string BaseUrl => $"{_options.Url.TrimEnd('/')}/storage/v1";

    private string ResolveBucketInternal(string? bucketOverride) => ResolveBucket(bucketOverride, _options.Bucket);

    // ─── Core storage operations ────────────────────────────────────────────────

    /// <summary>
    /// Uploads a file to Supabase Storage.
    /// </summary>
    protected override Task<StorageResult<UploadResult>> UploadCoreAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(request.BucketOverride);
        var url = SupabaseUrlBuilder.ObjectUrl(BaseUrl, bucket, request.Path);
        return SupabaseHttpHelper.UploadAsync(_httpClient, url, request, cancellationToken);
    }

    /// <summary>
    /// Downloads a file from Supabase Storage.
    /// </summary>
    protected override Task<StorageResult<Stream>> DownloadCoreAsync(
        DownloadRequest request, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(request.BucketOverride);
        var url = SupabaseUrlBuilder.ObjectUrl(BaseUrl, bucket, request.Path);
        return SupabaseHttpHelper.DownloadAsync(_httpClient, url, request, cancellationToken);
    }

    /// <summary>
    /// Deletes a file from Supabase Storage.
    /// </summary>
    protected override Task<StorageResult> DeleteCoreAsync(string path, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(null);
        var url = $"{BaseUrl}/object/{bucket}";
        return SupabaseHttpHelper.DeleteAsync(_httpClient, url, path, cancellationToken);
    }

    /// <summary>
    /// Checks if a file exists in Supabase Storage.
    /// </summary>
    protected override Task<StorageResult<bool>> ExistsCoreAsync(string path, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(null);
        var url = SupabaseUrlBuilder.ObjectInfoUrl(BaseUrl, bucket, path);
        return SupabaseHttpHelper.ExistsAsync(_httpClient, url, cancellationToken);
    }

    /// <summary>
    /// Gets the public URL for a file in Supabase Storage.
    /// </summary>
    protected override Task<StorageResult<string>> GetUrlCoreAsync(string path, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(null);
        var url = _options.CdnBaseUrl is not null
            ? $"{_options.CdnBaseUrl.TrimEnd('/')}/{path}"
            : SupabaseUrlBuilder.PublicObjectUrl(BaseUrl, bucket, path);

        return Task.FromResult(StorageResult<string>.Success(url));
    }

    /// <summary>
    /// Copies a file within Supabase Storage.
    /// </summary>
    protected override Task<StorageResult> CopyCoreAsync(
        string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(null);
        var url = SupabaseUrlBuilder.ObjectCopyUrl(BaseUrl);
        return SupabaseHttpHelper.CopyAsync(_httpClient, url, bucket, sourcePath, destinationPath, cancellationToken);
    }

    /// <summary>
    /// Gets metadata for a file in Supabase Storage.
    /// </summary>
    protected override Task<StorageResult<FileMetadata>> GetMetadataCoreAsync(
        string path, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(null);
        var url = SupabaseUrlBuilder.ObjectInfoUrl(BaseUrl, bucket, path);
        return SupabaseHttpHelper.GetMetadataAsync(_httpClient, url, path, cancellationToken);
    }

    /// <summary>
    /// Sets metadata for a file in Supabase Storage.
    /// </summary>
    protected override Task<StorageResult> SetMetadataCoreAsync(
        string path, IDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        Logger.LogWarning("[Supabase] SetMetadata is not natively supported. File must be re-uploaded with new metadata.");
        return Task.FromResult(StorageResult.Failure(
            "Supabase Storage does not support metadata updates without re-upload.",
            StorageErrorCode.NotSupported));
    }

    /// <summary>
    /// Lists files in Supabase Storage.
    /// </summary>
    protected override Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesCoreAsync(
        string? prefix, ListOptions? options, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(null);
        var url = SupabaseUrlBuilder.ObjectListUrl(BaseUrl, bucket);
        return SupabaseHttpHelper.ListFilesAsync(_httpClient, url, prefix, options, cancellationToken);
    }

    // ─── IResumableUploadProvider (native TUS protocol) ────────────────────────

    /// <summary>
    /// Starts a resumable upload session using the TUS protocol.
    /// </summary>
    public Task<StorageResult<ResumableUploadSession>> StartResumableUploadAsync(
        ResumableUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var bucket = ResolveBucketInternal(request.BucketOverride);
        return _tusHandler.StartAsync(request, bucket, cancellationToken);
    }

    /// <summary>
    /// Uploads a chunk in a resumable upload session.
    /// </summary>
    public Task<StorageResult<ChunkUploadResult>> UploadChunkAsync(
        ResumableChunkRequest request,
        CancellationToken cancellationToken = default)
        => _tusHandler.UploadChunkAsync(request, cancellationToken);

    /// <summary>
    /// Gets the status of a resumable upload session.
    /// </summary>
    public override Task<StorageResult<ResumableUploadStatus>> GetUploadStatusAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
        => _tusHandler.GetStatusAsync(uploadId, cancellationToken);

    /// <summary>
    /// Completes a resumable upload session.
    /// </summary>
    public Task<StorageResult<UploadResult>> CompleteResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
        => _tusHandler.CompleteAsync(uploadId, cancellationToken);

    /// <summary>
    /// Aborts a resumable upload session.
    /// </summary>
    public Task<StorageResult> AbortResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
        => _tusHandler.AbortAsync(uploadId, cancellationToken);

    // ─── IPresignedUrlProvider ──────────────────────────────────────────────────

    /// <summary>
    /// Gets a presigned upload URL for a file in Supabase Storage.
    /// </summary>
    public Task<StorageResult<string>> GetPresignedUploadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default)
        => GetSignedUrlAsync(path, expiration, cancellationToken);

    /// <summary>
    /// Gets a presigned download URL for a file in Supabase Storage.
    /// </summary>
    public Task<StorageResult<string>> GetPresignedDownloadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default)
        => GetSignedUrlAsync(path, expiration, cancellationToken);

    private Task<StorageResult<string>> GetSignedUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(null);
        var url = SupabaseUrlBuilder.ObjectSignUrl(BaseUrl, bucket, path);
        return SupabaseHttpHelper.GetSignedUrlAsync(_httpClient, url, _options.Url, expiration, cancellationToken);
    }
}
