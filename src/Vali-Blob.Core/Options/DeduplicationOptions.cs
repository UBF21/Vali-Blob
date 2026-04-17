namespace ValiBlob.Core.Options;

public sealed class DeduplicationOptions
{
    /// <summary>Whether deduplication is enabled. Opt-in: disabled by default.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>When true, checks for an existing file with the same hash before uploading.</summary>
    public bool CheckBeforeUpload { get; set; } = true;

    /// <summary>The metadata key used to store and look up the content hash.</summary>
    public string MetadataHashKey { get; set; } = "x-content-hash";
}
