namespace ValiBlob.Core.Options;

/// <summary>
/// Global configuration for resumable (multi-chunk) uploads across all providers.
/// Bind to configuration section "ValiBlob:ResumableUpload" or configure via
/// <c>builder.WithResumableUploads(o => { ... })</c>.
/// </summary>
public sealed class ResumableUploadOptions
{
    public const string SectionName = "ValiBlob:ResumableUpload";

    /// <summary>
    /// Default chunk size in bytes sent per UploadChunkAsync call when no per-request override is given.
    /// Default: 8 MB. AWS S3 and OCI require a minimum of 5 MB per part (except the last).
    /// </summary>
    public long DefaultChunkSizeBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    /// Minimum part size in bytes enforced before finalizing. AWS S3 requires ≥ 5 MB for all but the last part.
    /// Default: 5 MB.
    /// </summary>
    public long MinPartSizeBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// How long a resumable upload session is considered valid.
    /// Sessions older than this value are expired and removed from the store.
    /// Default: 24 hours.
    /// </summary>
    public TimeSpan SessionExpiration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Whether to compute and validate MD5 checksums per chunk when the provider supports it.
    /// Default: true.
    /// </summary>
    public bool EnableChecksumValidation { get; set; } = true;

    /// <summary>
    /// Maximum number of parts/chunks that can be uploaded concurrently (where supported).
    /// Set to 1 for strictly sequential uploads. Default: 1.
    /// </summary>
    public int MaxConcurrentChunks { get; set; } = 1;
}
