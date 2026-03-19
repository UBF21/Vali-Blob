using System.Collections.Generic;

namespace ValiBlob.Core.Models;

/// <summary>
/// Represents the state of an in-progress resumable upload session.
/// The session is stored by <see cref="Abstractions.IResumableSessionStore"/> and updated as chunks are uploaded.
/// </summary>
public sealed class ResumableUploadSession
{
    /// <summary>Unique identifier for this upload session.</summary>
    public string UploadId { get; set; } = string.Empty;

    /// <summary>Destination path in the storage provider.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Bucket/container override used when the session was created.</summary>
    public string? BucketOverride { get; set; }

    /// <summary>Total file size in bytes declared at session creation.</summary>
    public long TotalSize { get; set; }

    /// <summary>Number of bytes successfully received by the provider so far.</summary>
    public long BytesUploaded { get; set; }

    /// <summary>MIME content type of the file.</summary>
    public string? ContentType { get; set; }

    /// <summary>Custom metadata to attach to the stored file on completion.</summary>
    public IDictionary<string, string>? Metadata { get; set; }

    /// <summary>UTC time when the session was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC time when this session expires. Null means no expiration.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>True if <see cref="Abstractions.IResumableUploadProvider.AbortResumableUploadAsync"/> was called.</summary>
    public bool IsAborted { get; set; }

    /// <summary>True if <see cref="Abstractions.IResumableUploadProvider.CompleteResumableUploadAsync"/> has been called successfully.</summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Provider-specific state stored as key-value string pairs.
    /// Used internally by each provider to persist IDs, URLs, part ETags, block IDs, etc.
    /// </summary>
    public Dictionary<string, string> ProviderData { get; set; } = new(StringComparer.Ordinal);
}
