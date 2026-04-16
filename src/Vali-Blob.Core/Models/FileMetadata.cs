namespace ValiBlob.Core.Models;

public sealed class FileMetadata
{
    public required string Path { get; init; }
    public long SizeBytes { get; init; }
    public string? ContentType { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? ETag { get; init; }
    public IDictionary<string, string> CustomMetadata { get; init; } = new Dictionary<string, string>();
}
