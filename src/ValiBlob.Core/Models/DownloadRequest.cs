namespace ValiBlob.Core.Models;

public sealed class DownloadRequest
{
    public required StoragePath Path { get; init; }
    public DownloadRange? Range { get; init; }

    /// <summary>Overrides the bucket/container configured in options for this specific operation.</summary>
    public string? BucketOverride { get; init; }

    /// <summary>If false, skips automatic decryption even if the file was encrypted. Default: true.</summary>
    public bool AutoDecrypt { get; init; } = true;

    /// <summary>If false, skips automatic decompression even if the file was compressed. Default: true.</summary>
    public bool AutoDecompress { get; init; } = true;
}

public sealed class DownloadRange
{
    public long From { get; init; }
    public long? To { get; init; }
}
