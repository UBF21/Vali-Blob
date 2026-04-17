namespace ValiBlob.Core.Options;

public sealed class CdnOptions
{
    /// <summary>Base CDN URL, e.g. "https://cdn.example.com"</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Optional path prefix to strip from storage paths before appending to CDN base URL.</summary>
    public string? StripPrefix { get; set; }
}
