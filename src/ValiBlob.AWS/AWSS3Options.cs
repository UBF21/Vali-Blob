namespace ValiBlob.AWS;

public sealed class AWSS3Options
{
    public const string SectionName = "ValiBlob:AWS";

    public string Bucket { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public bool UseIAMRole { get; set; }
    public string? ServiceUrl { get; set; } // For MinIO compatibility
    public bool ForcePathStyle { get; set; } // For MinIO compatibility
    public string? CdnBaseUrl { get; set; }
    public int MultipartThresholdMb { get; set; } = 100;
    public int MultipartChunkSizeMb { get; set; } = 8;
}
