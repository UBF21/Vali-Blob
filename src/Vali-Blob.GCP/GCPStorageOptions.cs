namespace ValiBlob.GCP;

public sealed class GCPStorageOptions
{
    public const string SectionName = "ValiBlob:GCP";

    public string Bucket { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string? CredentialsPath { get; set; }
    /// <remarks>
    /// SECURITY: Do NOT set this field in appsettings.json or any committed config file.
    /// Use environment variables or Google Application Default Credentials instead:
    /// export GOOGLE_APPLICATION_CREDENTIALS=/path/to/key.json
    /// </remarks>
    public string? CredentialsJson { get; set; }
    public string? CdnBaseUrl { get; set; }
}
