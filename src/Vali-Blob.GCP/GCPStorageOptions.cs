namespace ValiBlob.GCP;

public sealed class GCPStorageOptions
{
    public const string SectionName = "ValiBlob:GCP";

    public string Bucket { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string? CredentialsPath { get; set; }
    public string? CredentialsJson { get; set; }
    public string? CdnBaseUrl { get; set; }
}
