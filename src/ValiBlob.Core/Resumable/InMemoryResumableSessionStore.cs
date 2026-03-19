using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;

namespace ValiBlob.Core.Resumable;

/// <summary>
/// Default in-memory implementation of <see cref="IResumableSessionStore"/>.
/// Sessions are kept in a <see cref="ConcurrentDictionary{TKey,TValue}"/> and are lost on process restart.
/// A background timer evicts expired sessions every 5 minutes.
/// <para>
/// For distributed or persistent scenarios implement <see cref="IResumableSessionStore"/> backed by
/// Redis, SQL Server, or similar and register it as a singleton:
/// <c>services.AddSingleton&lt;IResumableSessionStore, MyStore&gt;();</c>
/// </para>
/// </summary>
public sealed class InMemoryResumableSessionStore : IResumableSessionStore, IDisposable
{
    private readonly ConcurrentDictionary<string, ResumableUploadSession> _sessions
        = new(StringComparer.Ordinal);

    private readonly ResumableUploadOptions _options;
    private readonly Timer _cleanupTimer;

    public InMemoryResumableSessionStore(IOptions<ResumableUploadOptions> options)
    {
        _options = options.Value;
        _cleanupTimer = new Timer(EvictExpiredSessions, null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    private void EvictExpiredSessions(object? state)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _sessions)
        {
            if (kvp.Value.ExpiresAt.HasValue && kvp.Value.ExpiresAt.Value < now)
                _sessions.TryRemove(kvp.Key, out _);
        }
    }

    public void Dispose() => _cleanupTimer.Dispose();

    public Task SaveAsync(ResumableUploadSession session, CancellationToken cancellationToken = default)
    {
        _sessions[session.UploadId] = session;
        return Task.CompletedTask;
    }

    public Task<ResumableUploadSession?> GetAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(uploadId, out var session))
            return Task.FromResult<ResumableUploadSession?>(null);

        if (session.ExpiresAt.HasValue && session.ExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(uploadId, out _);
            return Task.FromResult<ResumableUploadSession?>(null);
        }

        return Task.FromResult<ResumableUploadSession?>(session);
    }

    public Task UpdateAsync(ResumableUploadSession session, CancellationToken cancellationToken = default)
    {
        _sessions[session.UploadId] = session;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        _sessions.TryRemove(uploadId, out _);
        return Task.CompletedTask;
    }
}
