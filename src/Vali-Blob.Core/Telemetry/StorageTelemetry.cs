using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ValiBlob.Core.Telemetry;

public static class StorageTelemetry
{
    public const string ActivitySourceName = "ValiBlob";
    public const string MeterName = "ValiBlob";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    // ─── Operation counters ───────────────────────────────────────────────────

    public static readonly Counter<long> UploadCounter =
        Meter.CreateCounter<long>("valiblob.uploads.total", "uploads", "Total number of upload operations");

    public static readonly Counter<long> DownloadCounter =
        Meter.CreateCounter<long>("valiblob.downloads.total", "downloads", "Total number of download operations");

    public static readonly Counter<long> DeleteCounter =
        Meter.CreateCounter<long>("valiblob.deletes.total", "deletes", "Total number of delete operations");

    public static readonly Counter<long> CopyCounter =
        Meter.CreateCounter<long>("valiblob.copies.total", "copies", "Total number of copy operations");

    public static readonly Counter<long> ErrorCounter =
        Meter.CreateCounter<long>("valiblob.errors.total", "errors", "Total number of failed operations");

    // ─── Resumable upload counters ────────────────────────────────────────────

    public static readonly Counter<long> ResumableUploadStartedCounter =
        Meter.CreateCounter<long>("valiblob.resumable.started.total", "uploads", "Total resumable upload sessions started");

    public static readonly Counter<long> ResumableChunkCounter =
        Meter.CreateCounter<long>("valiblob.resumable.chunks.total", "chunks", "Total resumable upload chunks received");

    public static readonly Counter<long> ResumableUploadCompletedCounter =
        Meter.CreateCounter<long>("valiblob.resumable.completed.total", "uploads", "Total resumable upload sessions completed");

    public static readonly Counter<long> ResumableUploadAbortedCounter =
        Meter.CreateCounter<long>("valiblob.resumable.aborted.total", "uploads", "Total resumable upload sessions aborted");

    // ─── Bytes counters ───────────────────────────────────────────────────────

    public static readonly Counter<long> UploadBytesCounter =
        Meter.CreateCounter<long>("valiblob.uploads.bytes", "bytes", "Total bytes uploaded");

    public static readonly Counter<long> DownloadBytesCounter =
        Meter.CreateCounter<long>("valiblob.downloads.bytes", "bytes", "Total bytes downloaded");

    public static readonly Counter<long> ResumableChunkBytesCounter =
        Meter.CreateCounter<long>("valiblob.resumable.bytes", "bytes", "Total bytes transferred via resumable uploads");

    // ─── Duration histograms ──────────────────────────────────────────────────

    public static readonly Histogram<double> UploadDuration =
        Meter.CreateHistogram<double>("valiblob.upload.duration_ms", "ms", "Upload operation duration in milliseconds");

    public static readonly Histogram<double> DownloadDuration =
        Meter.CreateHistogram<double>("valiblob.download.duration_ms", "ms", "Download operation duration in milliseconds");

    public static readonly Histogram<double> DeleteDuration =
        Meter.CreateHistogram<double>("valiblob.delete.duration_ms", "ms", "Delete operation duration in milliseconds");

    public static readonly Histogram<double> CopyDuration =
        Meter.CreateHistogram<double>("valiblob.copy.duration_ms", "ms", "Copy operation duration in milliseconds");

    // ─── Activity helpers ─────────────────────────────────────────────────────

    public static Activity? StartActivity(string operationName, string provider, string path)
    {
        var activity = ActivitySource.StartActivity($"storage.{operationName}", ActivityKind.Client);
        activity?.SetTag("storage.provider", provider);
        activity?.SetTag("storage.path", path);
        activity?.SetTag("storage.operation", operationName);
        return activity;
    }

    public static Activity? StartActivity(string operationName, string provider, string path, string? contentType)
    {
        var activity = StartActivity(operationName, provider, path);
        if (contentType is not null)
            activity?.SetTag("storage.content_type", contentType);
        return activity;
    }

    // ─── Record helpers ───────────────────────────────────────────────────────

    public static void RecordUploadSuccess(string provider, long bytes, double durationMs, string? contentType = null)
    {
        var tags = BuildTags(provider, contentType);
        UploadCounter.Add(1, tags);
        if (bytes > 0) UploadBytesCounter.Add(bytes, tags);
        UploadDuration.Record(durationMs, tags);
    }

    public static void RecordDownloadSuccess(string provider, long bytes, double durationMs, string? contentType = null)
    {
        var tags = BuildTags(provider, contentType);
        DownloadCounter.Add(1, tags);
        if (bytes > 0) DownloadBytesCounter.Add(bytes, tags);
        DownloadDuration.Record(durationMs, tags);
    }

    public static void RecordDeleteSuccess(string provider, double durationMs)
    {
        var tag = new KeyValuePair<string, object?>("provider", provider);
        DeleteCounter.Add(1, tag);
        DeleteDuration.Record(durationMs, tag);
    }

    /// <summary>Kept for backward compatibility — prefer <see cref="RecordDeleteSuccess"/>.</summary>
    public static void RecordDelete(string provider) =>
        DeleteCounter.Add(1, new KeyValuePair<string, object?>("provider", provider));

    public static void RecordCopySuccess(string provider, double durationMs)
    {
        var tag = new KeyValuePair<string, object?>("provider", provider);
        CopyCounter.Add(1, tag);
        CopyDuration.Record(durationMs, tag);
    }

    public static void RecordError(string provider, string operation)
    {
        ErrorCounter.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("operation", operation));
    }

    public static void RecordResumableStarted(string provider) =>
        ResumableUploadStartedCounter.Add(1, new KeyValuePair<string, object?>("provider", provider));

    public static void RecordResumableChunk(string provider, long bytes)
    {
        var tag = new KeyValuePair<string, object?>("provider", provider);
        ResumableChunkCounter.Add(1, tag);
        if (bytes > 0) ResumableChunkBytesCounter.Add(bytes, tag);
    }

    public static void RecordResumableCompleted(string provider) =>
        ResumableUploadCompletedCounter.Add(1, new KeyValuePair<string, object?>("provider", provider));

    public static void RecordResumableAborted(string provider) =>
        ResumableUploadAbortedCounter.Add(1, new KeyValuePair<string, object?>("provider", provider));

    // ─── Private ──────────────────────────────────────────────────────────────

    private static KeyValuePair<string, object?>[] BuildTags(string provider, string? contentType)
    {
        if (contentType is null)
            return new[] { new KeyValuePair<string, object?>("provider", provider) };

        return new[]
        {
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("content_type", contentType)
        };
    }
}
