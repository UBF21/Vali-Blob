using System.Collections.Concurrent;

namespace ValiBlob.Core.Pipeline.Middlewares;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IDeduplicationHashIndex"/>.
/// Reduces deduplication lookup from O(n) network calls to O(1).
/// Suitable for single-instance deployments; replace with a distributed implementation (e.g. Redis) for multi-node.
/// </summary>
public sealed class InMemoryDeduplicationHashIndex : IDeduplicationHashIndex
{
    // hash → path
    private readonly ConcurrentDictionary<string, string> _hashToPath = new(StringComparer.OrdinalIgnoreCase);
    // path → hash (reverse index for O(1) removal)
    private readonly ConcurrentDictionary<string, string> _pathToHash = new(StringComparer.OrdinalIgnoreCase);

    public Task<string?> FindPathByHashAsync(string hash, CancellationToken ct = default)
    {
        _hashToPath.TryGetValue(hash, out var path);
        return Task.FromResult<string?>(path);
    }

    public Task IndexAsync(string hash, string path, CancellationToken ct = default)
    {
        // If the path was previously indexed under a different hash, remove the stale entry.
        if (_pathToHash.TryGetValue(path, out var previousHash))
            _hashToPath.TryRemove(previousHash, out _);

        _hashToPath[hash] = path;
        _pathToHash[path] = hash;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string path, CancellationToken ct = default)
    {
        if (_pathToHash.TryRemove(path, out var hash))
            _hashToPath.TryRemove(hash, out _);

        return Task.CompletedTask;
    }
}
