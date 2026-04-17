namespace ValiBlob.Core.Options;

public sealed class ConflictResolutionOptions
{
    public const string SectionName = "ValiBlob:ConflictResolution";

    /// <summary>
    /// Maximum number of rename attempts when ConflictResolution is set to Rename.
    /// If a file with the original name and all numbered variants up to this limit exist,
    /// the upload will fail with a conflict error.
    /// Default: 100 attempts.
    /// </summary>
    public int MaxRenameAttempts { get; set; } = 100;
}
