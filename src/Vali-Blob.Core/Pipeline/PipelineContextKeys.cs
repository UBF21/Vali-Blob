namespace ValiBlob.Core.Pipeline;

/// <summary>
/// Strongly-typed constant keys for StoragePipelineContext.Items dictionary.
/// Eliminates magic strings and enables compile-time verification.
/// </summary>
public static class PipelineContextKeys
{
    /// <summary>Key for storing the SHA256 hash computed by deduplication middleware.</summary>
    public const string DeduplicationHash = "valiblob.dedup.hash";

    /// <summary>Key for indicating if file was identified as duplicate by deduplication middleware.</summary>
    public const string DeduplicationIsDuplicate = "valiblob.dedup.is_duplicate";

    /// <summary>Key for storing the detected MIME type by content-type detection middleware.</summary>
    public const string DetectedContentType = "valiblob.content_type.detected";

    /// <summary>Key for storing conflict resolution action (Overwrite, Rename, Fail).</summary>
    public const string ConflictResolutionAction = "valiblob.conflict.action";

    /// <summary>Key for storing the resolved file path after conflict resolution.</summary>
    public const string ConflictResolutionPath = "valiblob.conflict.resolved_path";

    /// <summary>Key for virus scan result (clean/infected/error).</summary>
    public const string VirusScanStatus = "valiblob.virus.status";

    /// <summary>Key for virus scan error details if scan failed.</summary>
    public const string VirusScanError = "valiblob.virus.error";
}
