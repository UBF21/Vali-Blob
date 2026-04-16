using System.Collections.Concurrent;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Options;

namespace ValiBlob.Core.Quota;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IStorageQuotaService"/>.
/// Usage counters are lost when the process restarts.
/// </summary>
public sealed class InMemoryStorageQuotaService : IStorageQuotaService
{
    private readonly ConcurrentDictionary<string, long> _usage = new ConcurrentDictionary<string, long>();
    private readonly QuotaOptions _options;

    public InMemoryStorageQuotaService(QuotaOptions options)
    {
        _options = options;
    }

    public Task<long> GetUsedBytesAsync(string scope, CancellationToken cancellationToken = default)
    {
        _usage.TryGetValue(scope, out var used);
        return Task.FromResult(used);
    }

    public Task RecordUploadAsync(string scope, long bytes, CancellationToken cancellationToken = default)
    {
        _usage.AddOrUpdate(scope, bytes, (_, existing) => existing + bytes);
        return Task.CompletedTask;
    }

    public Task RecordDeleteAsync(string scope, long bytes, CancellationToken cancellationToken = default)
    {
        _usage.AddOrUpdate(scope, 0L, (_, existing) => Math.Max(0L, existing - bytes));
        return Task.CompletedTask;
    }

    public Task<long?> GetQuotaLimitAsync(string scope, CancellationToken cancellationToken = default)
    {
        if (_options.Limits.TryGetValue(scope, out var limit))
            return Task.FromResult<long?>(limit);

        return Task.FromResult(_options.DefaultLimitBytes);
    }
}
