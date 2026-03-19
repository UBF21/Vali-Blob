namespace ValiBlob.Core.Options;

public sealed class EncryptionOptions
{
    public bool Enabled { get; set; }

    /// <summary>AES-256 key — must be exactly 32 bytes. Store in secrets, never in appsettings.</summary>
    public byte[]? Key { get; set; }

    /// <summary>
    /// AES Initialization Vector — 16 bytes. If null, a random IV is generated per upload.
    /// For deterministic encryption (e.g. deduplication) provide a fixed IV.
    /// </summary>
    public byte[]? IV { get; set; }
}
