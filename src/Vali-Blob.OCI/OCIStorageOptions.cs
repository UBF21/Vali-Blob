namespace ValiBlob.OCI;

public sealed class OCIStorageOptions
{
    public const string SectionName = "ValiBlob:OCI";

    public string Namespace { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string? TenancyId { get; set; }
    public string? UserId { get; set; }
    public string? Fingerprint { get; set; }
    public string? PrivateKeyPath { get; set; }
    public string? PrivateKeyContent { get; set; }
    public string? ServiceUrl { get; set; }
    public string? CdnBaseUrl { get; set; }
}
