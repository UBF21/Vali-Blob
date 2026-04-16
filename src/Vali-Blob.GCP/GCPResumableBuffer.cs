using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ValiBlob.GCP;

/// <summary>
/// Singleton that buffers GCP resumable upload chunks on the local filesystem.
/// Each active session writes chunks to a temporary file. On completion the file is
/// uploaded atomically via the GCS SDK. On abort the temp file is deleted.
/// <para>
/// NOTE: GCP Cloud Storage does not expose its internal resumable-upload URI through
/// the official .NET SDK's public API. This class provides reliable chunk-by-chunk
/// semantics using temp-file buffering. For very large files in memory-constrained
/// environments consider implementing a custom GCS resumable upload via the REST API.
/// </para>
/// </summary>
public sealed class GCPResumableBuffer
{
    // uploadId → temp file path
    private readonly ConcurrentDictionary<string, string> _tempPaths
        = new(StringComparer.Ordinal);

    // uploadId → sorted write log: offset → written length (for status queries)
    private readonly ConcurrentDictionary<string, long> _bytesWritten
        = new(StringComparer.Ordinal);

    public string CreateSession(string uploadId)
    {
        var tempPath = System.IO.Path.GetTempFileName();
        _tempPaths[uploadId] = tempPath;
        _bytesWritten[uploadId] = 0L;
        return tempPath;
    }

    public bool TryGetTempPath(string uploadId, out string tempPath)
        => _tempPaths.TryGetValue(uploadId, out tempPath!);

    public long GetBytesWritten(string uploadId)
        => _bytesWritten.TryGetValue(uploadId, out var n) ? n : 0L;

    public void AddBytesWritten(string uploadId, long count)
        => _bytesWritten.AddOrUpdate(uploadId, count, (_, existing) => existing + count);

    public void RemoveSession(string uploadId)
    {
        if (_tempPaths.TryRemove(uploadId, out var tempPath))
        {
            try { System.IO.File.Delete(tempPath); } catch { /* best effort */ }
        }
        _bytesWritten.TryRemove(uploadId, out _);
    }
}
