namespace ValiBlob.Core.Options;

public sealed class CompressionOptions
{
    public bool Enabled { get; set; } = true;
    public int MinSizeBytes { get; set; } = 1024; // Only compress > 1KB
    public IList<string> CompressibleContentTypes { get; set; } = new List<string>
    {
        "text/plain", "text/html", "text/css", "text/xml",
        "application/json", "application/xml", "application/javascript"
    };
}
