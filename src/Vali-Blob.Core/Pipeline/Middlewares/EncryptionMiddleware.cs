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
        if (_options.Enabled && (_options.Key is null || _options.Key.Length == 0))
            throw new InvalidOperationException("EncryptionOptions.Key must be set when encryption is enabled.");
    }

    public async Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next)
    {
        if (!_options.Enabled || context.Request.Options?.Encryption != Models.StorageEncryptionMode.ClientSide)
        {
            await next(context);
            return;
        }

        var (encryptedStream, iv) = await EncryptAsync(context.Request.Content, _options.Key);

        var metadata = new Dictionary<string, string>(
            context.Request.Metadata ?? new Dictionary<string, string>())
        {
            ["x-vali-encrypted"] = "AES-256-CBC",
            ["x-vali-iv"] = Convert.ToBase64String(iv)
        };

        context.Request = context.Request.WithContent(encryptedStream).WithMetadata(metadata);

        await next(context);
    }

    private static async Task<(Stream encrypted, byte[] iv)> EncryptAsync(Stream input, byte[]? key)
    {
        if (key is null || key.Length == 0)
            throw new InvalidOperationException("EncryptionOptions.Key must be set for client-side encryption.");

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var iv = aes.IV;

        byte[] encryptedBytes;
        using (var outputStream = new MemoryStream())
        {
            using (var cryptoStream = new CryptoStream(outputStream, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                await input.CopyToAsync(cryptoStream);
            }
            encryptedBytes = outputStream.ToArray();
        }

        return (new MemoryStream(encryptedBytes), iv);
    }
}
