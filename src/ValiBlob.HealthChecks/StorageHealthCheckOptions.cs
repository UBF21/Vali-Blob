namespace ValiBlob.HealthChecks;

public sealed class StorageHealthCheckOptions
{
    /// <summary>Prefix used for the probe list operation. Default: empty (root).</summary>
    public string? ProbePrefix { get; set; }

    /// <summary>Timeout for the health check operation.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}
