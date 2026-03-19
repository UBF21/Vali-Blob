namespace ValiBlob.Core.Models;

public sealed class BatchDeleteResult
{
    public int TotalRequested { get; init; }
    public int Deleted { get; init; }
    public int Failed { get; init; }
    public IReadOnlyList<BatchDeleteError> Errors { get; init; } = Array.Empty<BatchDeleteError>();
}

public sealed class BatchDeleteError
{
    public required string Path { get; init; }
    public required string Reason { get; init; }
}
