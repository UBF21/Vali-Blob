using System.Net.Http;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;

namespace ValiBlob.Core.Providers;

/// <summary>
/// Implements higher-level composite operations that are built entirely on top of
/// the <see cref="IStorageProvider"/> contract. These operations contain no
/// provider-specific logic and live here so that <see cref="BaseStorageProvider"/>
/// stays focused on the thin wiring between the abstract core methods and the
/// cross-cutting infrastructure (resilience, telemetry, events).
/// </summary>
internal static class StorageProviderCompositeOperations
{
    internal static async Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(
        IStorageProvider provider,
        IEnumerable<StoragePath> paths,
        CancellationToken cancellationToken)
    {
        var pathList = new List<StoragePath>(paths);
        var errors = new List<BatchDeleteError>();
        var deleted = 0;

        foreach (var path in pathList)
        {
            var result = await provider.DeleteAsync(path, cancellationToken);
            if (result.IsSuccess)
                deleted++;
            else
                errors.Add(new BatchDeleteError { Path = path, Reason = result.ErrorMessage ?? "Unknown error" });
        }

        return StorageResult<BatchDeleteResult>.Success(new BatchDeleteResult
        {
            TotalRequested = pathList.Count,
            Deleted = deleted,
            Failed = errors.Count,
            Errors = errors.AsReadOnly()
        });
    }

    internal static async IAsyncEnumerable<FileEntry> ListAllAsync(
        IStorageProvider provider,
        string? prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? continuationToken = null;

        do
        {
            var options = new ListOptions
            {
                MaxResults = 1000,
                ContinuationToken = continuationToken
            };

            var result = await provider.ListFilesAsync(prefix, options, cancellationToken);
            if (!result.IsSuccess || result.Value is null)
                yield break;

            foreach (var entry in result.Value)
                yield return entry;

            continuationToken = result.Value.Count < 1000
                ? null
                : GetNextContinuationToken(result.Value);

            if (result.Value.Count < 1000)
                break;
        }
        while (continuationToken != null);
    }

    internal static async Task<StorageResult> DeleteFolderAsync(
        IStorageProvider provider,
        ILogger logger,
        string providerName,
        string prefix,
        CancellationToken cancellationToken)
    {
        try
        {
            var listResult = new List<FileEntry>();
            await foreach (var entry in ListAllAsync(provider, prefix, cancellationToken).WithCancellation(cancellationToken))
                listResult.Add(entry);

            var paths = listResult.Select(e => StoragePath.From(e.Path)).ToList();

            if (paths.Count == 0)
                return StorageResult.Success();

            var deleteResult = await provider.DeleteManyAsync(paths, cancellationToken);
            if (!deleteResult.IsSuccess)
                return StorageResult.Failure(deleteResult.ErrorMessage ?? "Batch delete failed", deleteResult.ErrorCode);

            if (deleteResult.Value!.Failed > 0)
                return StorageResult.Failure($"Failed to delete {deleteResult.Value.Failed} of {deleteResult.Value.TotalRequested} files.");

            return StorageResult.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{Provider}] DeleteFolder failed for prefix {Prefix}", providerName, prefix);
            return StorageResult.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    internal static async Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(
        IStorageProvider provider,
        ILogger logger,
        string providerName,
        string? prefix,
        CancellationToken cancellationToken)
    {
        try
        {
            var listResult = await provider.ListFilesAsync(prefix, null, cancellationToken);
            if (!listResult.IsSuccess)
                return StorageResult<IReadOnlyList<string>>.Failure(listResult.ErrorMessage!, listResult.ErrorCode);

            var folders = new HashSet<string>(StringComparer.Ordinal);
            var prefixLength = prefix?.Length ?? 0;

            foreach (var entry in listResult.Value ?? Array.Empty<FileEntry>())
            {
                var relativePath = prefixLength > 0 && entry.Path.Length > prefixLength
                    ? entry.Path.Substring(prefixLength)
                    : entry.Path;

                var slashIndex = relativePath.IndexOf('/');
                if (slashIndex > 0)
                    folders.Add(relativePath.Substring(0, slashIndex));
            }

            var result = new List<string>(folders);
            result.Sort(StringComparer.Ordinal);
            return StorageResult<IReadOnlyList<string>>.Success(result.AsReadOnly());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{Provider}] ListFolders failed for prefix {Prefix}", providerName, prefix);
            return StorageResult<IReadOnlyList<string>>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    internal static async Task<StorageResult<UploadResult>> UploadFromUrlAsync(
        IStorageProvider provider,
        ILogger logger,
        string providerName,
        IReadOnlyList<string> allowedUploadHosts,
        Func<string, HttpClient> httpClientFactory,
        string sourceUrl,
        StoragePath destinationPath,
        string? bucketOverride,
        CancellationToken cancellationToken)
    {
        try
        {
            if (allowedUploadHosts.Count > 0)
            {
                if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ||
                    !allowedUploadHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
                {
                    return StorageResult<UploadResult>.Failure(
                        $"URL host is not in the allowed list: '{sourceUrl}'",
                        StorageErrorCode.ProviderError);
                }
            }

            using var httpClient = httpClientFactory.Invoke("vali-blob");
            using var response = await httpClient.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType;
            var contentLength = response.Content.Headers.ContentLength;
            var stream = await response.Content.ReadAsStreamAsync();

            var request = new UploadRequest
            {
                Path = destinationPath,
                Content = stream,
                ContentType = contentType,
                ContentLength = contentLength,
                BucketOverride = bucketOverride
            };

            return await provider.UploadAsync(request, null, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{Provider}] UploadFromUrl failed for url {Url}", providerName, sourceUrl);
            return StorageResult<UploadResult>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    internal static async Task<StorageResult<ResumableUploadStatus>> GetUploadStatusAsync(
        ILogger logger,
        string providerName,
        Func<IResumableSessionStore?> getSessionStore,
        string uploadId,
        CancellationToken cancellationToken)
    {
        try
        {
            var sessionStore = getSessionStore();
            if (sessionStore is null)
                throw new NotImplementedException($"{providerName} does not support resumable upload status retrieval.");

            var uploadSession = await sessionStore.GetAsync(uploadId, cancellationToken);
            if (uploadSession is null)
                return StorageResult<ResumableUploadStatus>.Failure(
                    $"Upload session '{uploadId}' not found or expired.", StorageErrorCode.FileNotFound);

            return StorageResult<ResumableUploadStatus>.Success(new ResumableUploadStatus
            {
                UploadId = uploadId,
                Path = uploadSession.Path,
                TotalSize = uploadSession.TotalSize,
                BytesUploaded = uploadSession.BytesUploaded,
                IsComplete = uploadSession.IsComplete,
                IsAborted = uploadSession.IsAborted,
                ExpiresAt = uploadSession.ExpiresAt
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{Provider}] GetUploadStatus failed for session {UploadId}", providerName, uploadId);
            return StorageResult<ResumableUploadStatus>.Failure(ex.Message, StorageErrorCode.ProviderError, ex);
        }
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static string? GetNextContinuationToken(IReadOnlyList<FileEntry> entries)
        => entries.Count > 0 ? entries[entries.Count - 1].Path : null;
}
