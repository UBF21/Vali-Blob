using System.Security.Cryptography;

namespace ValiBlob.Core.Resumable;

/// <summary>
/// Shared helper for computing and validating MD5 checksums on resumable upload chunks.
/// </summary>
public static class ChunkChecksumHelper
{
    /// <summary>
    /// Computes the base64-encoded MD5 of <paramref name="data"/>.
    /// </summary>
    public static string ComputeMd5Base64(byte[] data)
    {
#if NET5_0_OR_GREATER
        return Convert.ToBase64String(MD5.HashData(data));
#else
        using var md5 = MD5.Create();
        return Convert.ToBase64String(md5.ComputeHash(data));
#endif
    }

    /// <summary>
    /// Validates <paramref name="actualMd5Base64"/> against <paramref name="expectedMd5Base64"/>.
    /// Returns an error message if they differ, or <c>null</c> if they match.
    /// </summary>
    public static string? Validate(string actualMd5Base64, string expectedMd5Base64)
    {
        return string.Equals(actualMd5Base64, expectedMd5Base64, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"Chunk checksum mismatch. Expected: {expectedMd5Base64}, actual: {actualMd5Base64}.";
    }
}
