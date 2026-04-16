namespace ValiBlob.Azure;

public sealed class AzureBlobOptions
{
    public const string SectionName = "ValiBlob:Azure";

    public string? ConnectionString { get; set; }
    public string? AccountName { get; set; }
    public string? AccountKey { get; set; }
    public string Container { get; set; } = string.Empty;
    public string? ServiceUrl { get; set; }
    public string? CdnBaseUrl { get; set; }
    public int MultipartChunkSizeMb { get; set; } = 4;
    public bool CreateContainerIfNotExists { get; set; } = true;
}
