namespace ValiBlob.Core.Models;

/// <summary>Current status of a resumable upload session, returned by GetUploadStatusAsync.</summary>
public sealed class ResumableUploadStatus
{
    /// <summary>The upload session ID.</summary>
    public required string UploadId { get; init; }

    /// <summary>Destination path.</summary>
    public required string Path { get; init; }

    /// <summary>Total declared file size in bytes.</summary>
    public long TotalSize { get; init; }

    /// <summary>Number of bytes successfully received by the provider.</summary>
    public long BytesUploaded { get; init; }

    /// <summary>True when <see cref="BytesUploaded"/> equals <see cref="TotalSize"/> and complete has been called.</summary>
    public bool IsComplete { get; init; }

    /// <summary>True if the upload was aborted.</summary>
    public bool IsAborted { get; init; }

    /// <summary>UTC expiration time of the session. Null if no expiration.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Upload completion percentage (0–100).</summary>
    public double ProgressPercent => TotalSize > 0 ? (double)BytesUploaded / TotalSize * 100.0 : 0;
}
