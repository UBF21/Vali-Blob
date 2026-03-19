using System.Collections.Generic;

namespace ValiBlob.Core.Models;

/// <summary>Request to initiate a new resumable upload session.</summary>
public sealed class ResumableUploadRequest
{
    /// <summary>Destination path in the storage provider.</summary>
    public required StoragePath Path { get; init; }

    /// <summary>MIME content type of the file being uploaded.</summary>
    public string? ContentType { get; init; }

    /// <summary>Total file size in bytes. Required by most providers to initiate the session.</summary>
    public long TotalSize { get; init; }

    /// <summary>Custom metadata to attach to the stored file.</summary>
    public IDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Overrides the bucket/container configured in provider options for this upload.
    /// Useful for multi-tenant scenarios.
    /// </summary>
    public string? BucketOverride { get; init; }

    /// <summary>
    /// Per-request options that override the globally configured <see cref="Options.ResumableUploadOptions"/>.
    /// Leave null to use global defaults.
    /// </summary>
    public ResumableUploadRequestOptions? Options { get; init; }
}

/// <summary>Per-request overrides for resumable upload behavior.</summary>
public sealed class ResumableUploadRequestOptions
{
    /// <summary>Chunk size in bytes for this specific upload. Overrides <see cref="Options.ResumableUploadOptions.DefaultChunkSizeBytes"/>.</summary>
    public long? ChunkSizeBytes { get; init; }

    /// <summary>Session expiration for this specific upload. Overrides the global default.</summary>
    public TimeSpan? SessionExpiration { get; init; }
}
