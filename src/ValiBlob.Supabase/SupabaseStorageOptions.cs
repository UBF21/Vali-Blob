namespace ValiBlob.Supabase;

public sealed class SupabaseStorageOptions
{
    public const string SectionName = "ValiBlob:Supabase";

    /// <summary>Your Supabase project URL. e.g. https://xyzcompany.supabase.co</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Service role key or anon key for authentication.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Default bucket name.</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>Optional CDN base URL for public files.</summary>
    public string? CdnBaseUrl { get; set; }
}
