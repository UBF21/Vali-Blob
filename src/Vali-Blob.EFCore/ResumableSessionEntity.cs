using System;

namespace ValiBlob.EFCore;

/// <summary>
/// EF Core entity that persists a <see cref="ValiBlob.Core.Models.ResumableUploadSession"/> to a relational database.
/// Mapped to the <c>ValiBlob_ResumableSessions</c> table by <see cref="ValiResumableDbContext"/>.
/// </summary>
public class ResumableSessionEntity
{
    public string UploadId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? BucketOverride { get; set; }
    public long TotalSize { get; set; }
    public long BytesUploaded { get; set; }
    public string? ContentType { get; set; }

    /// <summary>JSON-serialized <c>IDictionary&lt;string,string&gt;</c> for custom file metadata.</summary>
    public string? MetadataJson { get; set; }

    /// <summary>JSON-serialized <c>Dictionary&lt;string,string&gt;</c> for provider-internal state.</summary>
    public string? ProviderDataJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsAborted { get; set; }
    public bool IsComplete { get; set; }
}
