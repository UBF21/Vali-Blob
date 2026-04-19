namespace ValiBlob.Core.Options;

public sealed class RateLimitOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>Maximum number of storage operations allowed per window per scope.</summary>
    public int MaxRequestsPerWindow { get; set; } = 100;

    /// <summary>Duration of the sliding window.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Optional scope key resolver. Defaults to BucketOverride ?? "global".
    /// Override to scope by tenant, user, IP, etc.
    /// </summary>
    public Func<string?, string>? ScopeResolver { get; set; }
}
