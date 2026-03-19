using ValiBlob.Core.Abstractions;

namespace ValiBlob.Core.Security;

/// <summary>
/// Default no-op virus scanner that approves all files.
/// Replace with a real implementation in production.
/// </summary>
public sealed class NoOpVirusScanner : IVirusScanner
{
    public Task<VirusScanResult> ScanAsync(Stream content, string? fileName, CancellationToken cancellationToken = default)
        => Task.FromResult(VirusScanResult.Clean("NoOp"));
}
