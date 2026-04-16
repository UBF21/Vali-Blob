namespace ValiBlob.Core.Abstractions;

public interface IVirusScanner
{
    /// <summary>
    /// Scans the given stream for malware.
    /// Returns a <see cref="VirusScanResult"/> indicating whether the content is clean.
    /// </summary>
    Task<VirusScanResult> ScanAsync(Stream content, string? fileName, CancellationToken cancellationToken = default);
}

public sealed class VirusScanResult
{
    public bool IsClean { get; init; }
    public string? ThreatName { get; init; }
    public string? ScannerName { get; init; }

    public static VirusScanResult Clean(string scannerName) =>
        new() { IsClean = true, ScannerName = scannerName };

    public static VirusScanResult Infected(string threatName, string scannerName) =>
        new() { IsClean = false, ThreatName = threatName, ScannerName = scannerName };
}
