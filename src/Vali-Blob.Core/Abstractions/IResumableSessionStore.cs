using ValiBlob.Core.Models;

namespace ValiBlob.Core.Abstractions;

/// <summary>
/// Stores resumable upload session state across requests.
/// The default implementation is in-memory (sessions are lost on process restart).
/// For distributed or persistent scenarios implement this interface with Redis, SQL Server, etc.
/// and register your implementation with: services.AddSingleton&lt;IResumableSessionStore, MyStore&gt;();
/// </summary>
public interface IResumableSessionStore
{
    /// <summary>Persists a new upload session.</summary>
    Task SaveAsync(ResumableUploadSession session, CancellationToken cancellationToken = default);

    /// <summary>Retrieves a session by its upload ID. Returns null if not found or expired.</summary>
    Task<ResumableUploadSession?> GetAsync(string uploadId, CancellationToken cancellationToken = default);

    /// <summary>Replaces an existing session with updated state.</summary>
    Task UpdateAsync(ResumableUploadSession session, CancellationToken cancellationToken = default);

    /// <summary>Removes a session (called on complete or abort).</summary>
    Task DeleteAsync(string uploadId, CancellationToken cancellationToken = default);
}
