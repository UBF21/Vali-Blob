using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Options;

namespace ValiBlob.Core.Pipeline.Middlewares;

public sealed class EncryptionMiddleware : IStorageMiddleware
{
    private readonly EncryptionOptions _options;

    public EncryptionMiddleware(IOptions<EncryptionOptions> options)
    {
        _options = options.Value;
    }

    public async Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next)
    {
        if (!_options.Enabled || context.Request.Options?.Encryption != Models.StorageEncryptionMode.ClientSide)
        {
            await next(context);
            return;
        }

        var encryptedStream = await EncryptAsync(context.Request.Content, _options.Key, _options.IV);

        var metadata = new Dictionary<string, string>(
            context.Request.Metadata ?? new Dictionary<string, string>())
        {
            ["x-vali-encrypted"] = "AES-256-CBC",
            ["x-vali-iv"] = Convert.ToBase64String(_options.IV ?? Array.Empty<byte>())
        };

        context.Request = context.Request.WithContent(encryptedStream).WithMetadata(metadata);

        await next(context);
    }

    private static async Task<Stream> EncryptAsync(Stream input, byte[]? key, byte[]? iv)
    {
        if (key is null || key.Length == 0)
            throw new InvalidOperationException("EncryptionOptions.Key must be set for client-side encryption.");

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv ?? aes.IV;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        byte[] encryptedBytes;
        using (var outputStream = new MemoryStream())
        {
            using (var cryptoStream = new CryptoStream(outputStream, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                await input.CopyToAsync(cryptoStream);
            }
            encryptedBytes = outputStream.ToArray();
        }

        return new MemoryStream(encryptedBytes);
    }
}
