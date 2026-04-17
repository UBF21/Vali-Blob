using System;
using System.Security.Cryptography;
using System.Text;

namespace ValiBlob.Core.Models.Crypto;

/// <summary>
/// Cryptographic extensions for StoragePath.
/// Provides hash-based path manipulation operations.
/// </summary>
public static class StoragePathCryptoExtensions
{
    /// <summary>
    /// Appends a short SHA-256 hash suffix to the filename (before extension).
    /// E.g. "photo.jpg" → "photo_a3f2b1c4.jpg"
    /// </summary>
    public static StoragePath WithHashSuffix(this StoragePath path, string content)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
        var shortHash = BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLowerInvariant();

        var pathStr = path.ToString();
        var ext = System.IO.Path.GetExtension(pathStr);
        var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(pathStr);
        var dir = System.IO.Path.GetDirectoryName(pathStr) ?? "";

        var newName = string.IsNullOrEmpty(dir)
            ? $"{nameWithoutExt}_{shortHash}{ext}"
            : $"{dir}/{nameWithoutExt}_{shortHash}{ext}";
        return StoragePath.From(newName);
    }
}
