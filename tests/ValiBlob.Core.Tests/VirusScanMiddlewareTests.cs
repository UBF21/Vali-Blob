using FluentAssertions;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Exceptions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Pipeline.Middlewares;
using ValiBlob.Core.Security;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class VirusScanMiddlewareTests
{
    private static StorageMiddlewareDelegate NoOpNext => _ => Task.CompletedTask;

    private static StoragePipelineContext MakeContext(byte[]? content = null, string path = "uploads/test.bin")
    {
        content ??= new byte[] { 1, 2, 3 };
        var request = new UploadRequest
        {
            Path = StoragePath.From(path),
            Content = new MemoryStream(content),
            ContentLength = content.Length
        };
        return new StoragePipelineContext(request);
    }

    // Inline infected scanner
    private sealed class AlwaysInfectedScanner : IVirusScanner
    {
        private readonly string _threatName;
        public AlwaysInfectedScanner(string threatName = "EICAR-Test-File") => _threatName = threatName;

        public Task<VirusScanResult> ScanAsync(Stream content, string? fileName, CancellationToken cancellationToken = default)
            => Task.FromResult(VirusScanResult.Infected(_threatName, "FakeScanner"));
    }

    // Inline clean scanner that captures stream state
    private sealed class CleanScannerWithPositionCapture : IVirusScanner
    {
        public long? StreamPositionAfterScan { get; private set; }

        public Task<VirusScanResult> ScanAsync(Stream content, string? fileName, CancellationToken cancellationToken = default)
        {
            // Read all bytes to advance the stream position
            var buffer = new byte[1024];
            while (content.Read(buffer, 0, buffer.Length) > 0) { }
            StreamPositionAfterScan = content.CanSeek ? content.Position : null;
            return Task.FromResult(VirusScanResult.Clean("TestScanner"));
        }
    }

    // 1. NoOpVirusScanner → always clean → middleware calls next
    [Fact]
    public async Task NoOpScanner_CleanResult_CallsNext()
    {
        var scanner = new NoOpVirusScanner();
        var mw = new VirusScanMiddleware(scanner);
        var ctx = MakeContext();

        var nextCalled = false;
        await mw.InvokeAsync(ctx, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue();
    }

    // 2. Infected scanner → throws StorageValidationException
    [Fact]
    public async Task InfectedScanner_ThrowsStorageValidationException()
    {
        var scanner = new AlwaysInfectedScanner();
        var mw = new VirusScanMiddleware(scanner);
        var ctx = MakeContext();

        var act = async () => await mw.InvokeAsync(ctx, NoOpNext);

        await act.Should().ThrowAsync<StorageValidationException>();
    }

    // 3. Infected scanner → context.IsCancelled == true
    [Fact]
    public async Task InfectedScanner_SetsCancelledOnContext()
    {
        var scanner = new AlwaysInfectedScanner();
        var mw = new VirusScanMiddleware(scanner);
        var ctx = MakeContext();

        try { await mw.InvokeAsync(ctx, NoOpNext); } catch (StorageValidationException) { }

        ctx.IsCancelled.Should().BeTrue();
    }

    // 4. Infected scanner → cancellation reason contains threat name
    [Fact]
    public async Task InfectedScanner_CancellationReasonContainsThreatName()
    {
        var threatName = "EICAR-Test-File";
        var scanner = new AlwaysInfectedScanner(threatName);
        var mw = new VirusScanMiddleware(scanner);
        var ctx = MakeContext();

        try { await mw.InvokeAsync(ctx, NoOpNext); } catch (StorageValidationException) { }

        ctx.CancellationReason.Should().Contain(threatName);
    }

    // 5. After clean scan, seekable stream is rewound
    [Fact]
    public async Task CleanScan_SeekableStream_IsRewoundAfterScan()
    {
        var captureScanner = new CleanScannerWithPositionCapture();
        var mw = new VirusScanMiddleware(captureScanner);
        var content = new byte[] { 10, 20, 30, 40, 50 };
        var ctx = MakeContext(content);

        long? positionInNext = null;
        await mw.InvokeAsync(ctx, c =>
        {
            positionInNext = c.Request.Content.CanSeek ? c.Request.Content.Position : (long?)null;
            return Task.CompletedTask;
        });

        // The scanner exhausted the stream; middleware should have rewound it
        positionInNext.Should().Be(0);
    }

    // 6. VirusScanResult.Clean has IsClean = true and correct ScannerName
    [Fact]
    public void VirusScanResult_Clean_HasCorrectProperties()
    {
        var result = VirusScanResult.Clean("MyScanner");

        result.IsClean.Should().BeTrue();
        result.ScannerName.Should().Be("MyScanner");
        result.ThreatName.Should().BeNull();
    }

    // 7. VirusScanResult.Infected has IsClean = false, ThreatName set
    [Fact]
    public void VirusScanResult_Infected_HasCorrectProperties()
    {
        var result = VirusScanResult.Infected("EICAR", "TestEngine");

        result.IsClean.Should().BeFalse();
        result.ThreatName.Should().Be("EICAR");
        result.ScannerName.Should().Be("TestEngine");
    }

    // 8. Custom injected scanner that always returns infected is used by the middleware
    [Fact]
    public async Task CustomInfectedScanner_CanBeInjected()
    {
        IVirusScanner customScanner = new AlwaysInfectedScanner("CustomThreat");
        var mw = new VirusScanMiddleware(customScanner);
        var ctx = MakeContext();

        StorageValidationException? caught = null;
        try { await mw.InvokeAsync(ctx, NoOpNext); }
        catch (StorageValidationException ex) { caught = ex; }

        caught.Should().NotBeNull();
        caught!.Errors.Should().Contain(e => e.Contains("CustomThreat"));
    }
}
