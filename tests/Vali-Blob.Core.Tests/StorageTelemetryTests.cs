using System.Diagnostics;
using FluentAssertions;
using ValiBlob.Core.Telemetry;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class StorageTelemetryTests : IDisposable
{
    private readonly ActivityListener _listener;
    private Activity? _lastActivity;

    public StorageTelemetryTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == StorageTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => _lastActivity = activity
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    // ─── Activity tests ──────────────────────────────────────────────────────

    [Fact]
    public void StartActivity_WithActiveListener_ReturnsNonNull()
    {
        using var activity = StorageTelemetry.StartActivity("upload", "InMemory", "test/file.txt");

        activity.Should().NotBeNull();
    }

    [Fact]
    public void StartActivity_WithContentType_SetsContentTypeTag()
    {
        using var activity = StorageTelemetry.StartActivity("upload", "InMemory", "test/file.txt", "image/png");

        activity.Should().NotBeNull();
        activity!.GetTagItem("storage.content_type").Should().Be("image/png");
    }

    [Fact]
    public void StartActivity_WithoutContentType_DoesNotSetContentTypeTag()
    {
        using var activity = StorageTelemetry.StartActivity("upload", "InMemory", "test/file.txt");

        activity.Should().NotBeNull();
        activity!.GetTagItem("storage.content_type").Should().BeNull();
    }

    [Fact]
    public void StartActivity_SetsProviderTag()
    {
        using var activity = StorageTelemetry.StartActivity("download", "GCP", "bucket/key.bin");

        activity.Should().NotBeNull();
        activity!.GetTagItem("storage.provider").Should().Be("GCP");
    }

    [Fact]
    public void StartActivity_SetsPathTag()
    {
        using var activity = StorageTelemetry.StartActivity("upload", "Azure", "container/blob.pdf");

        activity.Should().NotBeNull();
        activity!.GetTagItem("storage.path").Should().Be("container/blob.pdf");
    }

    // ─── Metrics record helpers — verify they do not throw ───────────────────

    [Fact]
    public void RecordUploadSuccess_DoesNotThrow()
    {
        var act = () => StorageTelemetry.RecordUploadSuccess("InMemory", 1024, 12.5, "application/pdf");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordUploadSuccess_WithoutContentType_DoesNotThrow()
    {
        var act = () => StorageTelemetry.RecordUploadSuccess("InMemory", 512, 5.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordDownloadSuccess_DoesNotThrow()
    {
        var act = () => StorageTelemetry.RecordDownloadSuccess("InMemory", 2048, 8.3, "video/mp4");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordDownloadSuccess_WithoutContentType_DoesNotThrow()
    {
        var act = () => StorageTelemetry.RecordDownloadSuccess("InMemory", 256, 2.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordDeleteSuccess_DoesNotThrow()
    {
        var act = () => StorageTelemetry.RecordDeleteSuccess("InMemory", 1.5);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordCopySuccess_DoesNotThrow()
    {
        var act = () => StorageTelemetry.RecordCopySuccess("InMemory", 3.2);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordError_DoesNotThrow()
    {
        var act = () => StorageTelemetry.RecordError("InMemory", "upload");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordResumableStarted_DoesNotThrow()
    {
        var act = () => StorageTelemetry.RecordResumableStarted("InMemory");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordResumableChunk_DoesNotThrow()
    {
        var act = () => StorageTelemetry.RecordResumableChunk("InMemory", 5 * 1024 * 1024);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordResumableChunk_ZeroBytes_DoesNotThrow()
    {
        var act = () => StorageTelemetry.RecordResumableChunk("InMemory", 0);
        act.Should().NotThrow();
    }
}
