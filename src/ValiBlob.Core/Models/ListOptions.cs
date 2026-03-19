namespace ValiBlob.Core.Models;

public sealed class ListOptions
{
    public int? MaxResults { get; init; }
    public string? ContinuationToken { get; init; }
    public bool IncludeDirectories { get; init; }
    public string? Delimiter { get; init; }
}
