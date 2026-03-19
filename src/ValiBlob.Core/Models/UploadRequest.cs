namespace ValiBlob.Core.Models;

public sealed class UploadRequest
{
    public required StoragePath Path { get; init; }
    public required Stream Content { get; init; }
    public string? ContentType { get; init; }
    public long? ContentLength { get; init; }
    public IDictionary<string, string>? Metadata { get; init; }
    public UploadOptions? Options { get; init; }

    /// <summary>
    /// Overrides the bucket/container configured in options for this specific operation.
    /// Useful for multi-tenant scenarios.
    /// </summary>
    public string? BucketOverride { get; init; }

    /// <summary>
    /// Determines how an upload should behave when a file already exists at the target path.
    /// </summary>
    public ConflictResolution ConflictResolution { get; init; } = ConflictResolution.Overwrite;

    public UploadRequest WithContent(Stream newContent) => new()
    {
        Path = Path,
        Content = newContent,
        ContentType = ContentType,
        ContentLength = ContentLength,
        Metadata = Metadata,
        Options = Options,
        BucketOverride = BucketOverride,
        ConflictResolution = ConflictResolution
    };

    public UploadRequest WithMetadata(IDictionary<string, string> newMetadata) => new()
    {
        Path = Path,
        Content = Content,
        ContentType = ContentType,
        ContentLength = ContentLength,
        Metadata = newMetadata,
        Options = Options,
        BucketOverride = BucketOverride,
        ConflictResolution = ConflictResolution
    };

    public UploadRequest WithContentType(string contentType) => new()
    {
        Path = Path,
        Content = Content,
        ContentType = contentType,
        ContentLength = ContentLength,
        Metadata = Metadata,
        Options = Options,
        BucketOverride = BucketOverride,
        ConflictResolution = ConflictResolution
    };

    public UploadRequest WithPath(StoragePath newPath) => new()
    {
        Path = newPath,
        Content = Content,
        ContentType = ContentType,
        ContentLength = ContentLength,
        Metadata = Metadata,
        Options = Options,
        BucketOverride = BucketOverride,
        ConflictResolution = ConflictResolution
    };
}

public sealed class UploadOptions
{
    public bool UseMultipart { get; init; }
    public int ChunkSizeMb { get; init; } = 8;
    public bool Overwrite { get; init; } = true;
    public StorageEncryptionMode Encryption { get; init; } = StorageEncryptionMode.None;
}

public enum StorageEncryptionMode
{
    None,
    ProviderManaged,
    ClientSide
}
