namespace ValiBlob.Core.Models;

/// <summary>Result returned after successfully uploading a single chunk.</summary>
public sealed class ChunkUploadResult
{
    /// <summary>The upload session ID.</summary>
    public required string UploadId { get; init; }

    /// <summary>Total bytes received by the provider after this chunk.</summary>
    public long BytesUploaded { get; init; }

    /// <summary>Total declared file size.</summary>
    public long TotalSize { get; init; }

    /// <summary>
    /// True when all bytes have been received and
    /// <see cref="Abstractions.IResumableUploadProvider.CompleteResumableUploadAsync"/> can be called.
    /// </summary>
    public bool IsReadyToComplete { get; init; }

    /// <summary>Upload completion percentage (0–100).</summary>
    public double ProgressPercent => TotalSize > 0 ? (double)BytesUploaded / TotalSize * 100.0 : 0;
}
