namespace ValiBlob.Core.Abstractions;

public interface IStorageQuotaService
{
    /// <summary>Returns current usage in bytes for the given scope (e.g. tenant ID or bucket prefix).</summary>
    Task<long> GetUsedBytesAsync(string scope, CancellationToken cancellationToken = default);

    /// <summary>Records uploaded bytes for the scope.</summary>
    Task RecordUploadAsync(string scope, long bytes, CancellationToken cancellationToken = default);

    /// <summary>Records deleted bytes for the scope.</summary>
    Task RecordDeleteAsync(string scope, long bytes, CancellationToken cancellationToken = default);

    /// <summary>Returns the quota limit in bytes for the scope, or null if unlimited.</summary>
    Task<long?> GetQuotaLimitAsync(string scope, CancellationToken cancellationToken = default);
}
