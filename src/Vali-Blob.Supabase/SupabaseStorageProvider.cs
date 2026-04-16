using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Providers;
using ValiBlob.Core.Telemetry;

namespace ValiBlob.Supabase;

public sealed class SupabaseStorageProvider : BaseStorageProvider, IPresignedUrlProvider, IResumableUploadProvider
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseStorageOptions _options;
    private readonly IResumableSessionStore _sessionStore;
    private readonly ResumableUploadOptions _resumableOptions;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SupabaseStorageProvider(
        HttpClient httpClient,
        IOptions<SupabaseStorageOptions> options,
        ILogger<SupabaseStorageProvider> logger,
        IOptions<ResilienceOptions> resilienceOptions,
        IOptions<EncryptionOptions> encryptionOptions,
        StoragePipelineBuilder pipeline,
        IResumableSessionStore sessionStore,
        IOptions<ResumableUploadOptions> resumableOptions)
        : base(logger, resilienceOptions, encryptionOptions, pipeline)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _sessionStore = sessionStore;
        _resumableOptions = resumableOptions.Value;
    }

    public override string ProviderName => "Supabase";

    private string BaseUrl => $"{_options.Url.TrimEnd('/')}/storage/v1";

    private string ResolveBucketInternal(string? bucketOverride) => ResolveBucket(bucketOverride, _options.Bucket);

    protected override async Task<StorageResult<UploadResult>> UploadCoreAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(request.BucketOverride);
        var url = $"{BaseUrl}/object/{bucket}/{request.Path}";

        using var content = new StreamContent(request.Content);
        content.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType ?? "application/octet-stream");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

        if (request.Metadata is not null)
        {
            foreach (var kvp in request.Metadata)
                httpRequest.Headers.TryAddWithoutValidation($"x-metadata-{kvp.Key}", kvp.Value);
        }

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            return StorageResult<UploadResult>.Failure($"Supabase upload failed ({response.StatusCode}): {error}", StorageErrorCode.ProviderError);
        }

        return StorageResult<UploadResult>.Success(new UploadResult
        {
            Path = request.Path,
            SizeBytes = request.ContentLength ?? 0
        });
    }

    protected override async Task<StorageResult<Stream>> DownloadCoreAsync(
        DownloadRequest request, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(request.BucketOverride);
        var url = $"{BaseUrl}/object/{bucket}/{request.Path}";

        var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);

        if (request.Range is not null)
        {
            var rangeHeader = request.Range.To.HasValue
                ? $"bytes={request.Range.From}-{request.Range.To}"
                : $"bytes={request.Range.From}-";
            httpRequest.Headers.TryAddWithoutValidation("Range", rangeHeader);
        }

        var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return StorageResult<Stream>.Failure($"File not found: {request.Path}", StorageErrorCode.FileNotFound);

        if (!response.IsSuccessStatusCode)
            return StorageResult<Stream>.Failure($"Download failed ({response.StatusCode})", StorageErrorCode.ProviderError);

        var stream = await response.Content.ReadAsStreamAsync();
        return StorageResult<Stream>.Success(stream);
    }

    protected override async Task<StorageResult> DeleteCoreAsync(string path, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(null);
        var url = $"{BaseUrl}/object/{bucket}";

        var body = JsonSerializer.Serialize(new { prefixes = new[] { path } }, JsonOptions);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Delete, url) { Content = content };
        var response = await _httpClient.SendAsync(request, cancellationToken);

        return response.IsSuccessStatusCode
            ? StorageResult.Success()
            : StorageResult.Failure($"Delete failed ({response.StatusCode})", StorageErrorCode.ProviderError);
    }

    protected override async Task<StorageResult<bool>> ExistsCoreAsync(string path, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(null);
        var url = $"{BaseUrl}/object/info/{bucket}/{path}";

        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return StorageResult<bool>.Success(false);

        return response.IsSuccessStatusCode
            ? StorageResult<bool>.Success(true)
            : StorageResult<bool>.Failure($"Exists check failed ({response.StatusCode})", StorageErrorCode.ProviderError);
    }

    protected override Task<StorageResult<string>> GetUrlCoreAsync(string path, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(null);
        var url = _options.CdnBaseUrl is not null
            ? $"{_options.CdnBaseUrl.TrimEnd('/')}/{path}"
            : $"{BaseUrl}/object/public/{bucket}/{path}";

        return Task.FromResult(StorageResult<string>.Success(url));
    }

    protected override async Task<StorageResult> CopyCoreAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(null);
        var url = $"{BaseUrl}/object/copy";

        var body = JsonSerializer.Serialize(new
        {
            bucketId = bucket,
            sourceKey = sourcePath,
            destinationKey = destinationPath
        }, JsonOptions);

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, cancellationToken);

        return response.IsSuccessStatusCode
            ? StorageResult.Success()
            : StorageResult.Failure($"Copy failed ({response.StatusCode})", StorageErrorCode.ProviderError);
    }

    protected override async Task<StorageResult<FileMetadata>> GetMetadataCoreAsync(string path, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(null);
        var url = $"{BaseUrl}/object/info/{bucket}/{path}";

        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return StorageResult<FileMetadata>.Failure($"File not found: {path}", StorageErrorCode.FileNotFound);

        if (!response.IsSuccessStatusCode)
            return StorageResult<FileMetadata>.Failure($"GetMetadata failed ({response.StatusCode})", StorageErrorCode.ProviderError);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return StorageResult<FileMetadata>.Success(new FileMetadata
        {
            Path = path,
            SizeBytes = root.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
            ContentType = root.TryGetProperty("mimetype", out var mime) ? mime.GetString() : null,
            ETag = root.TryGetProperty("etag", out var etag) ? etag.GetString() : null
        });
    }

    protected override async Task<StorageResult> SetMetadataCoreAsync(string path, IDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        // Supabase Storage does not support updating metadata after upload without re-uploading
        Logger.LogWarning("[Supabase] SetMetadata is not natively supported. File must be re-uploaded with new metadata.");
        return await Task.FromResult(StorageResult.Failure("Supabase Storage does not support metadata updates without re-upload.", StorageErrorCode.NotSupported));
    }

    protected override async Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesCoreAsync(
        string? prefix, ListOptions? options, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(null);
        var url = $"{BaseUrl}/object/list/{bucket}";

        var body = JsonSerializer.Serialize(new
        {
            prefix = prefix ?? string.Empty,
            limit = options?.MaxResults ?? 100,
            offset = 0,
            sortBy = new { column = "name", order = "asc" }
        }, JsonOptions);

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return StorageResult<IReadOnlyList<FileEntry>>.Failure($"ListFiles failed ({response.StatusCode})", StorageErrorCode.ProviderError);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var entries = new List<FileEntry>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var metadata = item.TryGetProperty("metadata", out var m) ? m : default;

            entries.Add(new FileEntry
            {
                Path = string.IsNullOrEmpty(prefix) ? name : $"{prefix!.TrimEnd('/')}/{name}",
                SizeBytes = metadata.ValueKind != JsonValueKind.Undefined && metadata.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
                ContentType = metadata.ValueKind != JsonValueKind.Undefined && metadata.TryGetProperty("mimetype", out var mimeProp) ? mimeProp.GetString() : null
            });
        }

        return StorageResult<IReadOnlyList<FileEntry>>.Success(entries.AsReadOnly());
    }

    // ─── IResumableUploadProvider (native TUS protocol) ─────────────────────
    // Supabase Storage supports TUS 1.0.0 at /storage/v1/upload/resumable

    private string TusBaseUrl => $"{_options.Url.TrimEnd('/')}/storage/v1/upload/resumable";

    private static string Base64Encode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    public async Task<StorageResult<ResumableUploadSession>> StartResumableUploadAsync(
        ResumableUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = StorageTelemetry.StartActivity("resumable.start", ProviderName, request.Path);
        try
        {
            var bucket = ResolveBucketInternal(request.BucketOverride);
            var tusMetadata = $"filename {Base64Encode(request.Path)},bucketName {Base64Encode(bucket)},contentType {Base64Encode(request.ContentType ?? "application/octet-stream")}";

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, TusBaseUrl);
            httpRequest.Headers.TryAddWithoutValidation("Tus-Resumable", "1.0.0");
            httpRequest.Headers.TryAddWithoutValidation("Upload-Length", request.TotalSize.ToString());
            httpRequest.Headers.TryAddWithoutValidation("Upload-Metadata", tusMetadata);
            httpRequest.Headers.TryAddWithoutValidation("x-upsert", "true");
            httpRequest.Content = new ByteArrayContent(Array.Empty<byte>());
            httpRequest.Content.Headers.ContentLength = 0;

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                StorageTelemetry.RecordError(ProviderName, "resumable.start");
                activity?.SetStatus(ActivityStatusCode.Error, $"TUS session creation failed ({response.StatusCode})");
                return StorageResult<ResumableUploadSession>.Failure($"TUS session creation failed ({response.StatusCode}): {err}", StorageErrorCode.ProviderError);
            }

            if (!response.Headers.TryGetValues("Location", out var locationValues))
            {
                StorageTelemetry.RecordError(ProviderName, "resumable.start");
                activity?.SetStatus(ActivityStatusCode.Error, "No Location header");
                return StorageResult<ResumableUploadSession>.Failure("TUS server did not return a Location header.", StorageErrorCode.ProviderError);
            }

            var tusUploadUrl = string.Join(string.Empty, locationValues);
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
                    ["tusUploadUrl"] = tusUploadUrl
                }
            };

            await _sessionStore.SaveAsync(session, cancellationToken);
            Logger.LogInformation("[Supabase] Started TUS resumable upload session {UploadId} for {Path}", session.UploadId, session.Path);
            StorageTelemetry.RecordResumableStarted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult<ResumableUploadSession>.Success(session);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.start");
            Logger.LogError(ex, "[Supabase] Failed to start TUS upload for {Path}", request.Path);
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

            var tusUploadUrl = session.ProviderData["tusUploadUrl"];

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
                if (read < chunkBytes.Length) Array.Resize(ref chunkBytes, read);
            }
            else
            {
                using var ms = new MemoryStream();
                await request.Data.CopyToAsync(ms, 81920, cancellationToken);
                chunkBytes = ms.ToArray();
            }

            var patchRequest = new HttpRequestMessage(new HttpMethod("PATCH"), tusUploadUrl)
            {
                Content = new ByteArrayContent(chunkBytes)
            };
            patchRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
            patchRequest.Content.Headers.ContentLength = chunkBytes.Length;
            patchRequest.Headers.TryAddWithoutValidation("Tus-Resumable", "1.0.0");
            patchRequest.Headers.TryAddWithoutValidation("Upload-Offset", request.Offset.ToString());

            var patchResponse = await _httpClient.SendAsync(patchRequest, cancellationToken);
            if (!patchResponse.IsSuccessStatusCode)
            {
                var err = await patchResponse.Content.ReadAsStringAsync();
                StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
                activity?.SetStatus(ActivityStatusCode.Error, $"TUS PATCH failed ({patchResponse.StatusCode})");
                return StorageResult<ChunkUploadResult>.Failure($"TUS PATCH failed ({patchResponse.StatusCode}): {err}", StorageErrorCode.ProviderError);
            }

            // TUS server returns Upload-Offset header with confirmed received bytes
            long confirmedOffset = request.Offset + chunkBytes.Length;
            if (patchResponse.Headers.TryGetValues("Upload-Offset", out var offsetValues))
                long.TryParse(string.Join(string.Empty, offsetValues), out confirmedOffset);

            session.BytesUploaded = confirmedOffset;
            await _sessionStore.UpdateAsync(session, cancellationToken);

            StorageTelemetry.RecordResumableChunk(ProviderName, chunkBytes.Length);
            activity?.SetStatus(ActivityStatusCode.Ok);

            var isReady = confirmedOffset >= session.TotalSize;
            return StorageResult<ChunkUploadResult>.Success(new ChunkUploadResult
            {
                UploadId = request.UploadId,
                BytesUploaded = confirmedOffset,
                TotalSize = session.TotalSize,
                IsReadyToComplete = isReady
            });
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.chunk");
            Logger.LogError(ex, "[Supabase] TUS chunk upload failed for session {UploadId}", request.UploadId);
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

            // Refresh offset from TUS server (HEAD request)
            if (!session.IsAborted && !session.IsComplete)
            {
                var headRequest = new HttpRequestMessage(HttpMethod.Head, session.ProviderData["tusUploadUrl"]);
                headRequest.Headers.TryAddWithoutValidation("Tus-Resumable", "1.0.0");
                var headResponse = await _httpClient.SendAsync(headRequest, cancellationToken);
                if (headResponse.IsSuccessStatusCode &&
                    headResponse.Headers.TryGetValues("Upload-Offset", out var offsetValues))
                {
                    if (long.TryParse(string.Join(string.Empty, offsetValues), out var serverOffset))
                    {
                        session.BytesUploaded = serverOffset;
                        await _sessionStore.UpdateAsync(session, cancellationToken);
                    }
                }
            }

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
            Logger.LogError(ex, "[Supabase] GetUploadStatus failed for session {UploadId}", uploadId);
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

            // For TUS: upload is complete when all bytes have been received.
            // The Supabase TUS server finalizes the upload automatically on the last PATCH.
            // Calling Complete is a no-op from the protocol perspective — just clean up the session.
            session.IsComplete = true;
            await _sessionStore.DeleteAsync(uploadId, cancellationToken);

            Logger.LogInformation("[Supabase] Marked TUS upload session {UploadId} as complete for {Path}", uploadId, session.Path);
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
            Logger.LogError(ex, "[Supabase] CompleteResumableUpload failed for session {UploadId}", uploadId);
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

            var tusUploadUrl = session.ProviderData["tusUploadUrl"];
            var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, tusUploadUrl);
            deleteRequest.Headers.TryAddWithoutValidation("Tus-Resumable", "1.0.0");

            // Best-effort DELETE — ignore failures (session may have already expired at the server)
            try { await _httpClient.SendAsync(deleteRequest, cancellationToken); } catch { /* ignore */ }

            await _sessionStore.DeleteAsync(uploadId, cancellationToken);
            Logger.LogInformation("[Supabase] Aborted TUS upload session {UploadId}", uploadId);
            StorageTelemetry.RecordResumableAborted(ProviderName);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return StorageResult.Success();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            StorageTelemetry.RecordError(ProviderName, "resumable.abort");
            Logger.LogError(ex, "[Supabase] AbortResumableUpload failed for session {UploadId}", uploadId);
            return StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    // ─── IPresignedUrlProvider ───────────────────────────────────────────────

    public async Task<StorageResult<string>> GetPresignedUploadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        return await GetSignedUrlAsync(path, expiration, cancellationToken);
    }

    public async Task<StorageResult<string>> GetPresignedDownloadUrlAsync(
        string path, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        return await GetSignedUrlAsync(path, expiration, cancellationToken);
    }

    private async Task<StorageResult<string>> GetSignedUrlAsync(string path, TimeSpan expiration, CancellationToken cancellationToken)
    {
        var bucket = ResolveBucketInternal(null);
        var url = $"{BaseUrl}/object/sign/{bucket}/{path}";

        var body = JsonSerializer.Serialize(new { expiresIn = (int)expiration.TotalSeconds }, JsonOptions);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return StorageResult<string>.Failure($"GetSignedUrl failed ({response.StatusCode})", StorageErrorCode.ProviderError);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("signedURL", out var signedUrl))
            return StorageResult<string>.Failure("Signed URL not found in response.", StorageErrorCode.ProviderError);

        var fullUrl = $"{_options.Url.TrimEnd('/')}{signedUrl.GetString()}";
        return StorageResult<string>.Success(fullUrl);
    }
}
