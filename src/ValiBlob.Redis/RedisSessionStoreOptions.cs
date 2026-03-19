namespace ValiBlob.Redis;

/// <summary>
/// Configuration options for <see cref="RedisResumableSessionStore"/>.
/// </summary>
public class RedisSessionStoreOptions
{
    /// <summary>
    /// Prefix applied to every Redis key. Default is <c>valiblob</c>.
    /// Full key format: <c>{KeyPrefix}:session:{uploadId}</c>
    /// </summary>
    public string KeyPrefix { get; set; } = "valiblob";

    /// <summary>
    /// Redis connection string (used when connecting without an external <c>IConnectionMultiplexer</c>).
    /// Default is <c>localhost:6379</c>.
    /// </summary>
    public string ConfigurationString { get; set; } = "localhost:6379";
}
