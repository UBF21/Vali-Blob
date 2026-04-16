using ValiBlob.Core.Models;

namespace ValiBlob.Core.Events;

public sealed class StorageEventContext
{
    public required string ProviderName { get; init; }
    public required string OperationType { get; init; } // "Upload", "Download", "Delete", etc.
    public string? Path { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public StorageErrorCode ErrorCode { get; init; }
    public TimeSpan Duration { get; init; }
    public long? FileSizeBytes { get; init; }
    public IDictionary<string, object> Extra { get; init; } = new Dictionary<string, object>();
}
