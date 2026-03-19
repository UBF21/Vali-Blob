namespace ValiBlob.Core.Abstractions;

public interface ICdnProvider
{
    /// <summary>Maps a storage path to its CDN URL.</summary>
    string GetCdnUrl(string storagePath);

    /// <summary>Invalidates the CDN cache for the given path.</summary>
    Task InvalidateCacheAsync(string storagePath, CancellationToken cancellationToken = default);
}
