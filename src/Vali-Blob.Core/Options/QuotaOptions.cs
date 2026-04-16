using ValiBlob.Core.Models;

namespace ValiBlob.Core.Options;

public class QuotaOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>Default quota limit in bytes. null means unlimited.</summary>
    public long? DefaultLimitBytes { get; set; }

    /// <summary>Per-scope quota overrides. Key is the scope name, value is the limit in bytes.</summary>
    public Dictionary<string, long> Limits { get; set; } = new Dictionary<string, long>();

    /// <summary>
    /// How to determine the scope from the upload request.
    /// Defaults to BucketOverride ?? "default".
    /// </summary>
    public Func<UploadRequest, string>? ScopeResolver { get; set; }
}
