namespace ValiBlob.Core.Models;

public sealed class FileEntry
{
    public required string Path { get; init; }
    public long SizeBytes { get; init; }
    public string? ContentType { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public string? ETag { get; init; }
    public bool IsDirectory { get; init; }
}
