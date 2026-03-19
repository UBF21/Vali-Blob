namespace ValiBlob.Core.Models;

public sealed class UploadResult
{
    public required string Path { get; init; }
    public string? ETag { get; init; }
    public long SizeBytes { get; init; }
    public string? Url { get; init; }
    public DateTimeOffset UploadedAt { get; init; } = DateTimeOffset.UtcNow;
}
