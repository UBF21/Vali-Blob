namespace ValiBlob.Core.Options;

/// <summary>
/// Enumeration of built-in storage provider types supported by Vali-Blob.
/// This prevents typos and configuration errors when selecting the default provider.
/// </summary>
public enum StorageProviderType
{
    /// <summary>No provider selected. Caller must explicitly specify provider on each operation.</summary>
    None = 0,

    /// <summary>Local filesystem provider (ValiBlob.Local).</summary>
    Local = 1,

    /// <summary>In-memory provider for testing (ValiBlob.Testing).</summary>
    InMemory = 2,

    /// <summary>Amazon S3 provider (ValiBlob.AWS).</summary>
    AWS = 3,

    /// <summary>Azure Blob Storage provider (ValiBlob.Azure).</summary>
    Azure = 4,

    /// <summary>Google Cloud Storage provider (ValiBlob.GCP).</summary>
    GCP = 5,

    /// <summary>Oracle Cloud Infrastructure Object Storage provider (ValiBlob.OCI).</summary>
    OCI = 6,

    /// <summary>Supabase Storage provider (ValiBlob.Supabase).</summary>
    Supabase = 7,

    /// <summary>Custom third-party provider registered with a specific key.</summary>
    Custom = 255
}
