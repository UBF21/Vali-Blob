namespace ValiBlob.Local.Options;

public sealed class LocalStorageOptions
{
    /// <summary>Root directory where files will be stored. Required.</summary>
    public string BasePath { get; set; } = string.Empty;

    /// <summary>
    /// If true, creates the BasePath directory if it does not exist.
    /// Default: true.
    /// </summary>
    public bool CreateIfNotExists { get; set; } = true;

    /// <summary>
    /// Base URL used to generate public URLs via GetUrlAsync.
    /// E.g. "http://localhost:5000/files". If empty, returns a file:// URI.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// If true, preserves the original directory structure inside BasePath.
    /// Default: true.
    /// </summary>
    public bool PreserveDirectoryStructure { get; set; } = true;
}
