using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Exceptions;
using ValiBlob.Core.Models;

namespace ValiBlob.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="IResumableSessionStore"/>.
/// Sessions are serialized as JSON and stored in Redis with an optional TTL derived from <c>ExpiresAt</c>.
/// Safe for multi-instance / distributed deployments.
/// </summary>
public sealed class RedisResumableSessionStore : IResumableSessionStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisSessionStoreOptions _options;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public RedisResumableSessionStore(
        IConnectionMultiplexer redis,
        IOptions<RedisSessionStoreOptions> options)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    private string BuildKey(string uploadId) =>
        $"{_options.KeyPrefix}:session:{uploadId}";

    public async Task SaveAsync(ResumableUploadSession session, CancellationToken cancellationToken = default)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));

        try
        {
            var db = _redis.GetDatabase();
            var key = BuildKey(session.UploadId);
            var json = JsonSerializer.Serialize(session, _jsonOptions);

            TimeSpan? expiry = null;
            if (session.ExpiresAt.HasValue)
            {
                var ttl = session.ExpiresAt.Value - DateTimeOffset.UtcNow;
                if (ttl > TimeSpan.Zero)
                    expiry = ttl;
            }

            await db.StringSetAsync(key, json, expiry).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            throw new StorageException($"Redis error saving session '{session.UploadId}'.", ex);
        }
    }

    public async Task<ResumableUploadSession?> GetAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(uploadId)) throw new ArgumentNullException(nameof(uploadId));

        try
        {
            var db = _redis.GetDatabase();
            var key = BuildKey(uploadId);
            var value = await db.StringGetAsync(key).ConfigureAwait(false);

            if (!value.HasValue)
                return null;

            var session = JsonSerializer.Deserialize<ResumableUploadSession>(value!, _jsonOptions);

            // Guard against stale sessions that slipped past TTL
            if (session?.ExpiresAt.HasValue == true && session.ExpiresAt.Value < DateTimeOffset.UtcNow)
            {
                await db.KeyDeleteAsync(key).ConfigureAwait(false);
                return null;
            }

            return session;
        }
        catch (RedisException)
        {
            // Graceful degradation: treat Redis failure as cache miss
            return null;
        }
    }

    public Task UpdateAsync(ResumableUploadSession session, CancellationToken cancellationToken = default)
        => SaveAsync(session, cancellationToken);

    public async Task DeleteAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(uploadId)) throw new ArgumentNullException(nameof(uploadId));

        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(BuildKey(uploadId)).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            throw new StorageException($"Redis error deleting session '{uploadId}'.", ex);
        }
    }
}
