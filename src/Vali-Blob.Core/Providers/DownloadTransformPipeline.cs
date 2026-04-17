using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;

namespace ValiBlob.Core.Providers;

/// <summary>
/// Pipeline for applying post-download transformations (decryption and decompression).
/// Transforms a raw downloaded stream based on metadata markers and DownloadRequest settings.
/// </summary>
internal class DownloadTransformPipeline
{
    private readonly EncryptionOptions _encryptionOptions;
    private readonly IStorageProvider _provider;

    public DownloadTransformPipeline(EncryptionOptions encryptionOptions, IStorageProvider provider)
    {
        _encryptionOptions = encryptionOptions;
        _provider = provider;
    }

    /// <summary>
    /// Applies decryption and/or decompression transforms to a downloaded stream.
    /// Order: Decrypt first, then decompress.
    /// </summary>
    public async Task<Stream> ApplyAsync(
        Stream rawStream,
        DownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        Stream current = rawStream;
        IDictionary<string, string>? customMetadata = null;

        // Fetch metadata once if either transform is requested
        if (request.AutoDecrypt || request.AutoDecompress)
        {
            var metaResult = await _provider.GetMetadataAsync(request.Path, cancellationToken).ConfigureAwait(false);
            if (metaResult.IsSuccess && metaResult.Value is not null)
                customMetadata = metaResult.Value.CustomMetadata;
        }

        if (customMetadata is null)
            return current;

        // 1. Decrypt first (if the file was encrypted)
        if (request.AutoDecrypt &&
            customMetadata.TryGetValue("x-vali-iv", out var ivBase64) &&
            !string.IsNullOrEmpty(ivBase64) &&
            _encryptionOptions.Enabled &&
            _encryptionOptions.Key is { Length: > 0 })
        {
            var iv = Convert.FromBase64String(ivBase64);
            current = await DecryptStreamAsync(current, _encryptionOptions.Key, iv).ConfigureAwait(false);
        }

        // 2. Decompress second (if the file was compressed)
        if (request.AutoDecompress &&
            customMetadata.TryGetValue("x-vali-compressed", out var compressionAlgo) &&
            string.Equals(compressionAlgo, "gzip", StringComparison.OrdinalIgnoreCase))
        {
            current = await DecompressGzipStreamAsync(current).ConfigureAwait(false);
        }

        return current;
    }

    private static async Task<Stream> DecryptStreamAsync(Stream encryptedStream, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Read entire encrypted content into memory first
        byte[] encryptedBytes;
        using (var buffer = new MemoryStream())
        {
            await encryptedStream.CopyToAsync(buffer).ConfigureAwait(false);
            encryptedBytes = buffer.ToArray();
        }

        using var encryptedMs = new MemoryStream(encryptedBytes);
        using var cryptoStream = new CryptoStream(encryptedMs, aes.CreateDecryptor(), CryptoStreamMode.Read);
        var output = new MemoryStream();
        await cryptoStream.CopyToAsync(output).ConfigureAwait(false);
        output.Position = 0;
        return output;
    }

    private static async Task<Stream> DecompressGzipStreamAsync(Stream compressedStream)
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(compressedStream, CompressionMode.Decompress, leaveOpen: true))
        {
            await gzip.CopyToAsync(output).ConfigureAwait(false);
        }
        output.Position = 0;
        return output;
    }
}
