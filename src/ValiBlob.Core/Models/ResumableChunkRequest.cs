namespace ValiBlob.Core.Models;

/// <summary>Request to upload a chunk within an active resumable upload session.</summary>
public sealed class ResumableChunkRequest
{
    /// <summary>The upload session ID returned by <see cref="Abstractions.IResumableUploadProvider.StartResumableUploadAsync"/>.</summary>
    public required string UploadId { get; init; }

    /// <summary>The chunk data stream, read from its current position.</summary>
    public required Stream Data { get; init; }

    /// <summary>Byte offset within the full file where this chunk starts (zero-based).</summary>
    public long Offset { get; init; }

    /// <summary>
    /// Explicit length of this chunk in bytes.
    /// If null, the entire remaining content of <see cref="Data"/> is used.
    /// </summary>
    public long? Length { get; init; }

    /// <summary>
    /// Optional base64-encoded MD5 hash of the chunk data provided by the caller.
    /// When <c>EnableChecksumValidation</c> is true and this value is set, ValiBlob validates
    /// the received bytes against this hash before forwarding the chunk to the provider.
    /// </summary>
    public string? ExpectedMd5 { get; init; }
}
