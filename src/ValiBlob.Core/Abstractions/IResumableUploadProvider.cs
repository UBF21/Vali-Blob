using ValiBlob.Core.Models;

namespace ValiBlob.Core.Abstractions;

/// <summary>
/// Provides resumable (multi-chunk) upload capabilities for large files.
/// AWS uses S3 Multipart Upload, Azure uses Block Blobs, Supabase uses native TUS protocol,
/// GCP uses locally-buffered chunks uploaded atomically, and OCI uses its Multipart Upload API.
/// </summary>
public interface IResumableUploadProvider
{
    /// <summary>
    /// Initiates a new resumable upload session and returns session information including the UploadId.
    /// Store the UploadId — it is required for all subsequent operations.
    /// </summary>
    Task<StorageResult<ResumableUploadSession>> StartResumableUploadAsync(
        ResumableUploadRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a chunk of data at the specified byte offset.
    /// Chunks must be uploaded in order (sequential offsets).
    /// </summary>
    Task<StorageResult<ChunkUploadResult>> UploadChunkAsync(
        ResumableChunkRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current upload status including how many bytes have been received.
    /// Use this to determine where to resume an interrupted upload.
    /// </summary>
    Task<StorageResult<ResumableUploadStatus>> GetUploadStatusAsync(
        string uploadId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes the upload after all chunks have been sent.
    /// Must be called once all bytes have been uploaded.
    /// </summary>
    Task<StorageResult<UploadResult>> CompleteResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aborts an in-progress upload and discards all uploaded data at the provider.
    /// </summary>
    Task<StorageResult> AbortResumableUploadAsync(
        string uploadId,
        CancellationToken cancellationToken = default);
}
