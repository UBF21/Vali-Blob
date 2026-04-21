using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;

namespace ValiBlob.EFCore;

/// <summary>
/// Entity Framework Core implementation of <see cref="IResumableSessionStore"/>.
/// Sessions are persisted to a relational database via <see cref="ValiResumableDbContext"/>.
/// </summary>
public sealed class EfCoreResumableSessionStore : IResumableSessionStore
{
    private readonly ValiResumableDbContext _dbContext;
    private readonly ILogger<EfCoreResumableSessionStore> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public EfCoreResumableSessionStore(
        ValiResumableDbContext dbContext,
        ILogger<EfCoreResumableSessionStore> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SaveAsync(ResumableUploadSession session, CancellationToken cancellationToken = default)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));

        try
        {
            var entity = MapToEntity(session);
            _dbContext.ResumableSessions.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving resumable session '{UploadId}' to database.", session.UploadId);
            throw;
        }
    }

    public async Task<ResumableUploadSession?> GetAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(uploadId)) throw new ArgumentNullException(nameof(uploadId));

        try
        {
            var entity = await _dbContext.ResumableSessions
                .FindAsync(new object[] { uploadId }, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
                return null;

            var session = MapToSession(entity);

            // Filter expired sessions
            if (session.ExpiresAt.HasValue && session.ExpiresAt.Value < DateTimeOffset.UtcNow)
            {
                _dbContext.ResumableSessions.Remove(entity);
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving resumable session '{UploadId}' from database.", uploadId);
            throw;
        }
    }

    public async Task UpdateAsync(ResumableUploadSession session, CancellationToken cancellationToken = default)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));

        try
        {
            var entity = await _dbContext.ResumableSessions
                .FindAsync(new object[] { session.UploadId }, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                _dbContext.ResumableSessions.Add(MapToEntity(session));
            }
            else
            {
                ApplyToEntity(session, entity);
                _dbContext.ResumableSessions.Update(entity);
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating resumable session '{UploadId}' in database.", session.UploadId);
            throw;
        }
    }

    public async Task DeleteAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(uploadId)) throw new ArgumentNullException(nameof(uploadId));

        try
        {
            var entity = await _dbContext.ResumableSessions
                .FindAsync(new object[] { uploadId }, cancellationToken)
                .ConfigureAwait(false);

            if (entity is not null)
            {
                _dbContext.ResumableSessions.Remove(entity);
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting resumable session '{UploadId}' from database.", uploadId);
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // Mapping helpers
    // -------------------------------------------------------------------------

    private static ResumableSessionEntity MapToEntity(ResumableUploadSession session)
    {
        var entity = new ResumableSessionEntity();
        ApplyToEntity(session, entity);
        return entity;
    }

    private static void ApplyToEntity(ResumableUploadSession session, ResumableSessionEntity entity)
    {
        entity.UploadId = session.UploadId;
        entity.Path = session.Path;
        entity.BucketOverride = session.BucketOverride;
        entity.TotalSize = session.TotalSize;
        entity.BytesUploaded = session.BytesUploaded;
        entity.ContentType = session.ContentType;
        entity.CreatedAt = session.CreatedAt;
        entity.ExpiresAt = session.ExpiresAt;
        entity.IsAborted = session.IsAborted;
        entity.IsComplete = session.IsComplete;
        entity.MetadataJson = session.Metadata is not null
            ? JsonSerializer.Serialize(session.Metadata, _jsonOptions)
            : null;
        entity.ProviderDataJson = session.ProviderData is { Count: > 0 }
            ? JsonSerializer.Serialize(session.ProviderData, _jsonOptions)
            : null;
    }

    private static ResumableUploadSession MapToSession(ResumableSessionEntity entity)
    {
        IDictionary<string, string>? metadata = null;
        if (!string.IsNullOrEmpty(entity.MetadataJson))
        {
            metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(
                entity.MetadataJson, _jsonOptions);
        }

        Dictionary<string, string> providerData = new(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(entity.ProviderDataJson))
        {
            var deserialized = JsonSerializer.Deserialize<Dictionary<string, string>>(
                entity.ProviderDataJson, _jsonOptions);
            if (deserialized is not null)
                providerData = new Dictionary<string, string>(deserialized, StringComparer.Ordinal);
        }

        return new ResumableUploadSession
        {
            UploadId = entity.UploadId,
            Path = entity.Path,
            BucketOverride = entity.BucketOverride,
            TotalSize = entity.TotalSize,
            BytesUploaded = entity.BytesUploaded,
            ContentType = entity.ContentType,
            Metadata = metadata,
            CreatedAt = entity.CreatedAt,
            ExpiresAt = entity.ExpiresAt,
            IsAborted = entity.IsAborted,
            IsComplete = entity.IsComplete,
            ProviderData = providerData
        };
    }
}
