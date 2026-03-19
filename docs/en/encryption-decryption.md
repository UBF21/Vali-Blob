# Encryption and Decryption

ValiBlob supports transparent AES-256-CBC client-side encryption. Files are encrypted before they leave your application and decrypted when they are downloaded. No changes are needed in your business code beyond the initial configuration.

---

## How it works

Encryption and decryption are handled entirely within ValiBlob, transparent to the caller.

```
Upload path:
  Your stream  →  [CompressionMiddleware]  →  EncryptionMiddleware  →  Cloud provider (encrypted bytes)

Download path:
  Cloud provider (encrypted bytes)  →  BaseStorageProvider (decrypts using IV from metadata)  →  Your code (original bytes)
```

### Upload

1. `EncryptionMiddleware` intercepts the upload stream.
2. A random 16-byte IV is generated for the file.
3. The stream is encrypted with AES-256-CBC using your configured key and the generated IV.
4. The IV is stored in the file metadata under `x-vali-iv` (base64-encoded).
5. A marker `x-vali-encrypted: AES-256-CBC` is also written to metadata.
6. The encrypted bytes are sent to the storage provider.

### Download

1. `BaseStorageProvider` fetches the file and its metadata.
2. It detects the `x-vali-iv` and `x-vali-encrypted` metadata keys.
3. It reads the IV and decrypts the content with the configured key.
4. Your code receives the original, unencrypted stream.

The IV is per-file: every upload generates a different IV. Two uploads of the same content with the same key produce different ciphertexts.

---

## Configuration

### DI registration

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(p => p
        .UseValidation()
        .UseCompression()
        .UseEncryption(e =>
        {
            e.Enabled = true;
            e.Key = Convert.FromBase64String(
                builder.Configuration["ValiBlob:EncryptionKey"]!);
            // e.IV = null  →  random IV per upload (recommended)
        })
    );
```

### `EncryptionOptions` reference

| Property | Type | Default | Description |
|---|---|---|---|
| `Enabled` | `bool` | `false` | Must be explicitly set to `true` to activate encryption |
| `Key` | `byte[]?` | `null` | AES-256 key — must be exactly 32 bytes. Store in secrets, never in `appsettings.json` |
| `IV` | `byte[]?` | `null` | AES IV — 16 bytes. If `null`, a random IV is generated per upload (recommended) |

---

## Key generation

Generate a cryptographically secure 32-byte key once and store it in your secret store:

```csharp
using System.Security.Cryptography;

var key = new byte[32];
RandomNumberGenerator.Fill(key);
Console.WriteLine(Convert.ToBase64String(key));
// Example output: "k3Xr9pZw2mNqT7yVbL4sHcOuE6FdAiJg8RnW0ePxKYM="
// Store this value in Azure Key Vault, AWS Secrets Manager, or environment variable
```

Alternatively, using the `Aes` class:

```csharp
using var aes = Aes.Create();
aes.KeySize = 256;
aes.GenerateKey();
Console.WriteLine("Key: " + Convert.ToBase64String(aes.Key));
```

---

## Key management

Never hardcode the encryption key in `appsettings.json` committed to source control.

### Environment variable (containers / CI)

```bash
ValiBlob__EncryptionKey=k3Xr9pZw2mNqT7yVbL4sHcOuE6FdAiJg8RnW0ePxKYM=
```

```csharp
e.Key = Convert.FromBase64String(Environment.GetEnvironmentVariable("ValiBlob__EncryptionKey")!);
```

### ASP.NET Core User Secrets (local development)

```bash
dotnet user-secrets set "ValiBlob:EncryptionKey" "k3Xr9pZw2mNqT7yVbL4sHcOuE6FdAiJg8RnW0ePxKYM="
```

### Azure Key Vault

```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri("https://my-vault.vault.azure.net/"),
    new DefaultAzureCredential());
```

Store the key as a secret named `ValiBlob--EncryptionKey`.

### AWS Secrets Manager

```csharp
builder.Configuration.AddSecretsManager(region: RegionEndpoint.USEast1, configurator: opts =>
{
    opts.SecretFilter = entry => entry.Name.StartsWith("valiblob/");
    opts.KeyGenerator = (entry, key) => key.Replace("valiblob/", "").Replace("/", ":");
});
```

---

## Full round-trip example

```csharp
// Upload — encrypted automatically by the pipeline
var uploadResult = await _storage.UploadAsync(new UploadRequest
{
    Path = StoragePath.From("documents", "contract.pdf"),
    Content = fileStream,
    ContentType = "application/pdf"
});

if (!uploadResult.IsSuccess)
    throw new Exception($"Upload failed: {uploadResult.ErrorMessage}");

// Download — decrypted automatically by the provider
var downloadResult = await _storage.DownloadAsync(new DownloadRequest
{
    Path = StoragePath.From("documents", "contract.pdf")
});

if (!downloadResult.IsSuccess)
    throw new Exception($"Download failed: {downloadResult.ErrorMessage}");

// downloadResult.Value contains the original unencrypted bytes
using var output = File.Create("contract-local.pdf");
await downloadResult.Value!.CopyToAsync(output);
```

---

## Encryption and compression combined

Both middlewares can be active simultaneously. The pipeline always applies them in the correct order:

```
Upload:   compress  →  encrypt  →  store
Download: fetch  →  decrypt  →  decompress  →  return
```

This means compression is effective (it runs on the original content, before encryption converts it to pseudo-random bytes). Both transformations are transparent to the caller.

```csharp
builder.Services
    .AddValiBlob()
    .UseAWS()
    .WithPipeline(p => p
        .UseValidation()
        .UseCompression()   // 1. Compress first
        .UseEncryption(e => // 2. Encrypt the compressed bytes
        {
            e.Enabled = true;
            e.Key = Convert.FromBase64String(builder.Configuration["ValiBlob:EncryptionKey"]!);
        })
    );
```

> Registering encryption before compression compresses already-encrypted data, which is essentially random bytes and will not compress. Always place `.UseCompression()` before `.UseEncryption()`.

---

## What is stored in metadata

When a file is uploaded with encryption enabled, ValiBlob writes the following entries into the file's metadata:

| Metadata key | Value | Description |
|---|---|---|
| `x-vali-encrypted` | `AES-256-CBC` | Indicates the encryption algorithm used |
| `x-vali-iv` | Base64-encoded 16-byte IV | The initialization vector required for decryption |

The encryption key itself is never stored. Only the IV is persisted — the key must remain available in your configuration.

---

## Fixed IV vs random IV

| Mode | Configuration | Security | Use case |
|---|---|---|---|
| Random IV per upload (default) | `IV = null` | Strong — identical files produce different ciphertexts | Recommended for all production scenarios |
| Fixed IV | `IV = new byte[16] { ... }` | Weaker — identical files produce identical ciphertexts | Deterministic deduplication only; understand the trade-off |

> Using a fixed IV significantly weakens encryption when the same key is reused across many files. Prefer random IV for production.

---

## Key rotation

To rotate the encryption key, re-encrypt existing files:

1. Configure the old key.
2. Download the file — the provider decrypts it with the old key.
3. Configure the new key.
4. Re-upload the decrypted content — the pipeline encrypts it with the new key.
5. Delete the original file only after confirming the re-upload succeeded.

Never delete the old key until all files encrypted with it have been rotated.

---

## Security properties

- **Algorithm:** AES-256-CBC with PKCS7 padding
- **Key size:** 256 bits (32 bytes)
- **IV size:** 128 bits (16 bytes), generated with `System.Security.Cryptography.Aes`
- **IV uniqueness:** One IV per upload; never reused across files (when using the default random mode)
- **Key storage:** Never persisted — only the IV is stored in file metadata
- **Provider-agnostic:** Works identically with AWS, Azure, GCP, OCI, Supabase, Local, and InMemory providers

---

## Limitations

- **Range downloads on encrypted files:** The provider must decrypt the full content before applying a byte range. Range downloads on encrypted files therefore read and decrypt the entire file, then return the requested segment.
- **Memory usage for large files:** Decryption currently buffers the full content in memory. Avoid encrypting very large files if memory pressure is a concern.
- **No server-side encryption bypass:** Client-side encryption is separate from any server-side encryption your cloud provider offers. Both can be active simultaneously, providing a defense-in-depth posture, but they are independent.
