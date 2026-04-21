namespace ValiBlob.Core.Pipeline.Middlewares;

/// <summary>
/// Provides O(1) hash-to-path lookups for deduplication, replacing the N+1 metadata scan.
/// </summary>
public interface IDeduplicationHashIndex
{
    /// <summary>Returns the stored path for the given content hash, or null if not indexed.</summary>
    Task<string?> FindPathByHashAsync(string hash, CancellationToken ct = default);

    /// <summary>Indexes a hash → path mapping after a successful upload.</summary>
    Task IndexAsync(string hash, string path, CancellationToken ct = default);

    /// <summary>Removes the index entry for a given path (e.g. on deletion).</summary>
    Task RemoveAsync(string path, CancellationToken ct = default);
}
